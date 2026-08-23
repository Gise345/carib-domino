# ADR 0024 — In-game voice chat (Unity Vivox)

- **Status:** Accepted
- **Date:** 2026-08-22
- **Scope:** `functions`, `net`, `ui`, `core`, `infra`
- **Relates:** [0021](0021-ota-updates.md) (OTA flags),
  [0022](0022-admin-and-moderation.md) (admin spine, bans, audit),
  [0023](0023-chat-and-moderation.md) (chat, guest entitlement, moderation queue)

## Context

Dominoes is a talking game. The banter across the table is most of the reason people
play it in company rather than alone, and typed chat does not carry it. ADR 0023 built
the moderation spine for text and explicitly deferred voice to "a later milestone"
while deciding its entitlement rule up front (§3). This ADR is that milestone.

The project has no audio layer at all today: `_Project/Audio/` is empty and there is
not one `AudioSource` in the codebase. So the choice of voice SDK also decides what
the first audio infrastructure in the game looks like.

## Decision

### 1. Unity Vivox, on cost

Three SDKs were considered. **Vivox is free to 5,000 peak concurrent users**, which
covers the whole launch year at our projected load.

**Photon Voice 2 was rejected despite being the tighter engineering fit.** It bills
on a **separate AppId with a separate CCU meter** from Fusion, so every player in a
voice match burns 2 CCU — one Fusion, one Voice. Its free tier is 20 CCU and
development-only, meaning voice would cost money from the day it ships ($95/mo at
500 CCU, $185/mo at 1,000). Agora bills per participant-minute, which is cheap at
launch and punishing at scale.

The engineering cost of not choosing Photon is real but bounded: Vivox is a second
live-service vendor and needs its own token-minting path. We accept that in exchange
for a free launch year.

### 2. Voice does not ride on Photon

Three planes stay separate: **Fusion** carries gameplay, **Vivox** carries audio,
**Firebase** is the gate. They are stitched by one identifier — the **Photon session
name**, which is already the chat `roomId` (ADR 0023 §2). The Vivox channel is
derived from it, so a voice channel spans a whole series exactly as its chat room does.

### 3. The entitlement rule is ADR 0023 §3 — and a guest does not even listen

Guests may not speak. This is not re-decided here: `assertNotGuest` already refuses
with *"Create a free account to use chat and voice."*, and `ChatEntitlement.CanUseVoice`
already computes it client-side. Voice adds two gates of its own to the same chain
(ban and mute are inherited as-is): **microphone permission** and **room scope**.

Voice does depart from chat in one place. A guest may *read* chat, but a guest does
**not connect to the voice channel at all** — they neither speak nor listen. Chat's
read-access is safe precisely because text you can see is text you can report; a
voice you have no participant handle for cannot be reported. Never connecting a guest
is also the cheaper answer, since a connected listener consumes a Vivox concurrent
user exactly as a speaker does.

A moderator mute is the opposite case: it takes the player's voice, not their ears.
They stay connected and keep following the table.

### 4. Tokens are minted by a Cloud Function, never by the client

Two callables, because the SDK asks two different questions:

- **`joinVoiceRoom`** — called once when the deal lands. Mirrors `joinChatRoom` line
  for line: same self-claim transaction, same gate chain (`assertNotGuest` →
  `assertNotBanned` → `assertNotMuted` → membership). Returns the channel name, the
  `canSpeak`/`canListen` verdict, and the non-secret Vivox connection settings, so
  **no Vivox configuration ships in the client at all**.
- **`mintVivoxToken`** — called by our `IVivoxTokenProvider` implementation each time
  the SDK needs a credential (`login`, then `join`). A Vivox access token is a
  **single-use, per-action** credential with a ~90 s TTL, not a session token, so
  there is nothing to refresh: once you are in the channel the token is spent. A
  45-minute Partner series re-mints nothing. Re-authorising per action also means a
  player banned mid-match loses voice at their next token request rather than at the
  end of the session.

> **The security-critical rule.** `IVivoxTokenProvider.GetTokenAsync` hands the
> *client's* `fromUserUri` and `channelUri` to the server. `mintVivoxToken` must
> **discard both** and rebuild them from the authenticated uid and the room the
> caller is verified to be a member of. Signing what the client asks for would make
> the callable an oracle that mints a token to join any channel as any user —
> a total bypass of this ADR and of ADR 0023. This is the single most important
> line in the design, and it is pinned by a test.

We deliberately do **not** adopt Unity Authentication with a Custom ID provider,
which is Unity's documented happy path. It would introduce a second identity per
player alongside Firebase, add a UGS server call to the login path, and put a
1-hour token refresh loop in our hands — a failure mode we would own forever, in
exchange for nothing.

The Vivox channel name is a **hash** of the `roomId`, not the room code itself.
Photon session names are case-sensitive but Vivox folds case-variant channel names
together, so `ABC123` and `abc123` — two genuinely different Photon rooms — would
share one voice channel and put strangers in each other's ears. Hashing also keeps
the human-guessable room code off Vivox's wire. Debuggability is preserved by
storing `voiceChannel` on the `/chatRooms/{roomId}` doc.

Channels are joined **audio-only**. Vivox's own text capability would create a
second, entirely unmoderated text channel running parallel to the Firestore chat
that ADR 0023 built the whole moderation spine around.

### 5. Launch scope is friends-only, behind an OTA flag

Voice ships **off** (`feature_voice_enabled = false`) and, when switched on, scoped
to **code-joined rooms only** (`voice_allowed_modes = "private"`). Random matchmaking
— strangers — stays text-only until moderation is proven in production, because voice
has no transcript and so no cheap after-the-fact review.

Two further scope tokens exist unused, as levers to pull deliberately rather than
code to write later: `partner` opens Partner tables of any origin (a silent partner
breaks 2-v-2 play in a way a silent opponent does not), and `random` opens
matchmaking outright. Both put players in earshot of strangers, which is the decision
being deferred — so neither is in the default.

Scope is **Remote Config**, not a hardcoded client check, so it widens without a
store build (ADR 0021). Both flags fail closed: a failed fetch leaves voice off.

### 6. Per-player mute is local and unpersisted

`VivoxParticipant.MutePlayerLocally()` is listener-side: muting someone stops *you*
hearing them and does not affect anyone else at the table. For v1 it lasts the
session and is not persisted, so there is **no blocklist collection and no new
`firestore.rules` block**. A persisted blocklist is a bigger decision — it needs its
own data model, its own privacy story, and an answer for what happens when two
blocked players are matchmade together — and is deferred.

### 7. No audio is ever recorded, buffered, or uploaded

A voice report carries **metadata only**: reporter, reported, room, matches, mode,
reason, timestamp. Reports land in the existing `/chatReports` collection with a
`kind: 'chat' | 'voice'` discriminator, so there is one moderation queue and no new
admin callables.

Recording other players' voices — even a 30-second rolling buffer held only until a
report — is a materially different product: it needs explicit consent copy, a
retention policy, GDPR disclosure, and storage. It buys stronger evidence and we may
want it later, so the report document shape leaves room for an optional `audioRef`
without a migration. It is not v1.

## Data model

No new collections. Voice reuses:

| Path | Change |
|---|---|
| `/chatRooms/{roomId}` | none — membership is read to authorise a voice token |
| `/chatReports/{reportId}` | gains `kind: 'chat' \| 'voice'`; voice reports carry no transcript |
| `/chatMutes/{uid}` | none — a chat mute silences voice too |
| `/bans/{uid}` | none |

## Consequences

- **Positive:** free to 5,000 PCU; one entitlement rule and one moderation queue
  shared with chat; every voice action re-authorised server-side; scope widens OTA;
  no audio stored, so the privacy answer stays simple in both app stores.
- **Cost:** a second live-service vendor and the first UPM live-service packages in
  the project (`com.unity.services.vivox`, `com.unity.services.core`). One callable
  invocation per Vivox action, which is a handful per player per match. Metadata-only
  reports are weaker evidence than a transcript — repeat-report counts, not a single
  report, are what will justify action.
- **Infra (manual, like ADR 0023's TTL policy):** link the Unity project to a Unity
  Gaming Services org, enable Vivox, and load the token-signing key into Secret
  Manager. The key must never reach the client or the repo — note the precedent to
  avoid: `PhotonAppSettings.asset` is git-tracked with a plaintext Fusion AppId.
  Linking also writes `cloudProjectId` into `ProjectSettings.asset`; commit that
  deliberately rather than as drive-by noise.
- **Identity leak, accepted:** the Vivox `PlayerId` is the Firebase uid, so it is
  visible to the other three players. This leaks nothing new — every chat message
  document already carries `senderUid` and is read by all four room members — and
  every uid-keyed collection is deny-all. If it ever matters, the upgrade is a
  room-scoped opaque alias resolved server-side, with no schema migration.
- **A warning for whoever adds game audio:** voice forces the iOS audio session to
  `PlayAndRecord`. There is no music or SFX in the project yet, so nothing breaks
  today — but once there is, SFX will route to the earpiece instead of the speaker
  unless the session category is configured to coexist.
- **Naming wart:** voice reuses `/chatRooms` and `/chatReports`, so both names are
  now slightly misleading. Renaming would break the deployed rules, the TTL
  `fieldOverrides`, and every existing report's `roomId`. Not worth it.
- **Deliberately deferred:** persisted per-player blocklists, push-to-talk as an
  option, positional/3D audio, Unity Safe Voice (paid AI moderation), voice in random
  matchmaking, and recorded evidence.
