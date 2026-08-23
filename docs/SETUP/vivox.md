# Setup you own: in-game voice (Vivox)

Voice runs on **Unity Vivox** — free to 5,000 peak concurrent users. Design and
rationale: [ADR 0024](../DECISIONS/0024-in-game-voice.md).

Repo side so far: the pure entitlement gate (`Core/Voice/`), the OTA scope flags,
the Vivox token primitives, and the two callables (`joinVoiceRoom`,
`mintVivoxToken`). **None of it can run until the steps below are done** — both
callables refuse with `voice-disabled` while the credentials are empty, which is
deliberate: better an honest refusal than a token the client cannot use.

---

## One-time setup

### 1. Link the Unity project to Unity Gaming Services

In the Unity Editor: **Edit ▸ Project Settings ▸ Services** → create or link a UGS
project.

This writes `cloudProjectId`, `projectName` and `organizationId` into
`unity/ProjectSettings/ProjectSettings.asset` and `UnityConnectSettings.asset`.
None of those are secret, but they are surprising diffs — commit them on their own
as `chore(infra)`, not mixed into a feature branch.

### 2. Enable Vivox and read the credentials

**Unity Dashboard ▸ your project ▸ Vivox ▸ Credentials.** You need four values:

| Value | Example | Secret? |
|---|---|---|
| Issuer | `pose-carib-domino-dev` | no |
| Domain | `tla.vivox.com` | no |
| Server / API endpoint | `https://unity.vivox.com/appconfig/...` | no |
| Token signing key | a long random string | **YES** |

### 3. Put the signing key in Secret Manager — never in the repo

```bash
firebase functions:secrets:set VIVOX_TOKEN_KEY
# paste the key at the prompt
```

The key can mint a credential to join any channel as any user. It is bound to
`mintVivoxToken` only, and it never crosses the wire to a client.

### 4. Put the non-secret settings in `functions/.env`

`.env.*` is already gitignored.

```
VIVOX_ISSUER=pose-carib-domino-dev
VIVOX_DOMAIN=tla.vivox.com
VIVOX_SERVER=https://unity.vivox.com/appconfig/...
```

These are handed to the game by `joinVoiceRoom` at runtime rather than compiled
into the client, so switching environments is a config change, not a store build.

> **Do not put any Vivox value in a Unity asset.**
> `unity/Assets/Photon/Fusion/Resources/PhotonAppSettings.asset` carries a
> plaintext Photon AppId in version control to this day. That is the mistake this
> arrangement exists to avoid repeating.

### 5. Deploy

```bash
cd functions && npm run deploy
firebase deploy --only firestore:rules   # adds /voiceTokenLimits deny-all
```

---

## Switching voice on

Voice ships **off**. Both flags live in **Firebase console ▸ Remote Config**:

| Key | Default | Meaning |
|---|---|---|
| `feature_voice_enabled` | `false` | master switch; fails closed |
| `voice_allowed_modes` | `private` | which tables get voice |

`voice_allowed_modes` is a comma-separated list of:

- **`private`** — code-joined rooms. The launch scope: the table is friends.
- **`partner`** — Partner tables of *any* origin, matchmade ones included.
- **`random`** — random matchmaking outright.

`partner` and `random` both put players in earshot of strangers, and voice leaves
no transcript to review after the fact. Widen only once moderation is proven.

A client picks up a change on its **next cold start** past the 1-hour Remote
Config cache (`RemoteConfigService.CacheExpiry`).

---

## Verifying it works

You need **two physical devices**. Two Unity Editors contend for one microphone and
the second gets silence — you would be testing nothing and concluding it works.
The Android emulator has no usable mic either, and the project builds ARM only.

Sideload the APK on two handsets, sign in with **real accounts** (a guest is
refused by design), join one room code from both, and check:

1. Each hears the other, and the speaking ring lights the right seat.
2. Muting a player on device A silences them for A only — not for everyone.
3. A guest account gets a locked mic and never connects to Vivox at all.
4. Denying the OS mic prompt leaves the game fully playable.
5. Backgrounding the app releases the mic (the OS recording indicator clears).
6. Both devices on speakerphone, a metre apart, at volume — the echo test. Vivox
   has its own AEC but mobile AEC is device-specific; test on the cheapest target
   handset, not a flagship.

---

## Costs and limits

- Free to **5,000 peak concurrent users**. A PCU is counted while *connected to a
  channel*, which is why nothing connects until the deal lands, guests never
  connect, and scope is friends-only at launch.
- **Safe Voice** (Unity's AI voice moderation) is a **separate paid add-on** and is
  deliberately out of scope. v1 reporting is metadata-only: no audio is ever
  recorded, buffered, or uploaded.
