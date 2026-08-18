# Setup you own: TestFlight + Play internal testing

The pipelines are built (`codemagic.yaml`, `scripts/build-android.ps1`,
`unity/Assets/Editor/BuildScript.cs`). What's left needs your accounts. Work top to
bottom; each part ends with the exact variable names the pipeline expects.

Design decisions behind this: [ADR 0020](../DECISIONS/0020-release-pipeline.md).

---

## Part 0 — The upload key (done, but you must back it up)

I generated your Android upload key. It is **outside the repo** and **not in any
backup you have yet**:

```
~/.config/invovibe/pose-upload.keystore        the key
~/.config/invovibe/android-signing.env         its passwords
```

| | |
|---|---|
| Alias | `pose-upload` |
| Algorithm | RSA 2048, valid 10,950 days (~30 years) |
| SHA-1 | `55:1F:ED:54:22:B8:F0:9D:75:A0:16:2A:F7:6C:F3:05:23:13:1A:82` |
| SHA-256 | `71:ED:78:52:3F:B7:63:CD:F3:89:31:3A:AD:F3:86:43:EE:C2:AC:BB:21:19:87:FE:EB:D0:B8:67:8D:7F:AA:1B` |
| Facebook key hash | `VR/tVCK48J11oBYq92zzBSMTGoI=` |

> **Do this now:** copy both files into your password manager or an encrypted
> backup. With Play App Signing enrolled, a lost *upload* key is recoverable via a
> Google support reset — but that costs days, and the reset does not exist if you
> ever opt out of Play App Signing.

---

## Part 1 — Google Play

### 1.1 Enrol in Play App Signing

Play Console → your app → **Test and release ▸ Setup ▸ App signing**. Take the
default (Google generates and manages the app signing key). Your upload key above
is what you sign with; Google re-signs with the app signing key.

Afterwards that page shows the **app signing certificate** fingerprints. They are
*different* from the upload fingerprints above, and they are the ones that matter
for anything a tester installs from Play — see 1.4 and Part 4.

### 1.2 First upload must be manual

Play refuses API uploads until a release has been created in the console once.

```powershell
./scripts/build-android.ps1 -Version 0.1.0
```

Close the Unity Editor first — batch mode cannot open a project the Editor has
locked; the script checks and tells you. It writes `unity/Builds/pose.aab`.

Upload it at **Testing ▸ Internal testing ▸ Create new release**, add testers by
email list, and roll out.

### 1.3 Service account so Codemagic can upload after that

1. Google Cloud Console → IAM → create a service account, download its JSON key.
2. Play Console → **Users and permissions ▸ Invite new user**, paste the service
   account email.
3. Grant **Release apps to testing tracks** on this app only.

### 1.4 Re-register the *app signing* fingerprints

Once 1.1 is done, take the SHA-1 and SHA-256 from the App signing page and add
them to Firebase (Part 4) and Facebook (Part 5) **in addition to** the upload
fingerprints. Miss this and Facebook login works on your sideloaded APK and fails
for every tester who installs from Play.

---

## Part 2 — App Store Connect

### 2.1 Bundle ID

The pipeline builds `com.invovibe.posedominoes` — it must match the App ID in your
Apple Developer account and the app record in App Store Connect. Enable **Push
Notifications** on the App ID if you keep FCM in the stack.

### 2.2 App Store Connect API key

**Users and Access ▸ Integrations ▸ App Store Connect API ▸ Team Keys ▸ +**.
Role **App Manager**. Download the `.p8` — Apple lets you download it once.

Note the **Issuer ID** (top of that page), the **Key ID**, and your **Team ID**
(Developer portal ▸ Membership).

### 2.3 Export compliance

Already handled: `IosPostProcess.cs` writes `ITSAppUsesNonExemptEncryption = false`
into the exported Info.plist, so uploads go straight to testers instead of parking
on a compliance question. This is correct as long as Pose's only cryptography stays
HTTPS to Firebase/Photon. If that ever changes, change the key.

---

## Part 3 — Codemagic

Connect `github.com/Gise345/carib-domino`, then create these **environment groups**
(Settings ▸ Environment variables). Mark every secret **Secure**.

| Group | Variable | Value |
|---|---|---|
| `unity` | `UNITY_EMAIL` | your Unity account email |
| | `UNITY_PASSWORD` | your Unity account password |
| | `UNITY_SERIAL` | Plus/Pro serial — see the caveat below |
| `firebase` | `GOOGLE_SERVICES_JSON` | base64 of `unity/Assets/google-services.json` |
| | `GOOGLE_SERVICE_INFO_PLIST` | base64 of `unity/Assets/GoogleService-Info.plist` |
| `android-signing` | `POSE_KEYSTORE` | base64 of `pose-upload.keystore` |
| | `POSE_KEYSTORE_PASS` | from `android-signing.env` |
| | `POSE_KEY_ALIAS` | `pose-upload` |
| | `POSE_KEY_PASS` | from `android-signing.env` |
| `google-play` | `GCLOUD_SERVICE_ACCOUNT_CREDENTIALS` | the whole Play service-account JSON |
| `appstore` | `APP_STORE_CONNECT_ISSUER_ID` | from 2.2 |
| | `APP_STORE_CONNECT_KEY_IDENTIFIER` | the Key ID from 2.2 |
| | `APP_STORE_CONNECT_PRIVATE_KEY` | contents of the `.p8` |
| | `CERTIFICATE_PRIVATE_KEY` | leave empty and let Codemagic generate one, or paste your own |
| | `POSE_APPLE_TEAM_ID` | from 2.2 |

Base64 on Windows (PowerShell):

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:USERPROFILE\.config\invovibe\pose-upload.keystore")) | Set-Clipboard
```

### Triggering

Both workflows are tag-triggered so a release is always a deliberate act:

```bash
git tag android-v0.1.0; git push origin android-v0.1.0
git tag ios-v0.1.0;     git push origin ios-v0.1.0
```

`versionCode` / `CFBundleVersion` come from Codemagic's `$PROJECT_BUILD_NUMBER`, so
they always increase. `bundleVersion` comes from ProjectSettings unless you set
`POSE_BUILD_VERSION`.

> **Unity licence caveat — check this before you burn build minutes.** Headless
> Unity on CI activates cleanly with a **Plus/Pro** serial. Unity Personal has no
> serial to paste and needs the manual `.alf` / `.ulf` licence-file dance instead.
> If you're on Personal, say so and I'll swap the activation step for that flow.

---

## Part 4 — Firebase fingerprints

Firebase Console → **carib-domino ▸ Project settings ▸ Your apps ▸ Android app ▸
Add fingerprint**. Add all three:

- debug key (already there if sign-in works from the Editor)
- **upload** key SHA-1 + SHA-256 (Part 0)
- **app signing** key SHA-1 + SHA-256 (Part 1.4)

Then re-download `google-services.json` into `unity/Assets/` and refresh the
`GOOGLE_SERVICES_JSON` Codemagic variable.

The iOS app already exists in the same Firebase project — the committed
`GoogleService-Info.plist` carries bundle ID `com.invovibe.posedominoes`.

---

## Part 5 — Facebook, iOS side

`facebook-and-production.md` covered the Android platform. For TestFlight:

1. Meta app → **Settings ▸ Basic ▸ Add Platform ▸ iOS**.
2. **Bundle ID** `com.invovibe.posedominoes`. Turn on **Single Sign On**.
3. Android **Key Hashes**: add the upload key hash from Part 0 *and* the app
   signing key hash (derive it the same way from the certificate Play shows you).

`IosPostProcess.cs` warns in the build log if `FacebookAppID` is missing from the
exported Info.plist — watch for that on the first iOS build.

---

## Known risks in this first pass

These are real and unverified — better you see them than discover them in a failed
build.

1. **The Facebook SDK pulled in the legacy Android Support Library.** Importing it
   added `com.android.support:*:25.3.1` to `mainTemplate.gradle`. Jetifier is on, so
   it may survive, but 25.3.1 predates AndroidX and often breaks manifest merging at
   `targetSdk 36`. If Gradle fails with duplicate classes or a merger error, the fix
   is to delete those four lines and let the Firebase AndroidX artifacts satisfy the
   dependency.
2. **`targetSdk` moved 34 → 36.** Play enforces 36 for new uploads from
   2026-08-31. This changes runtime behaviour — predictive back and edge-to-edge
   display are mandatory on Android 15+. Test the first internal build on a real
   Android 15 or 16 device before adding testers.
3. **Codemagic image contents.** The pipeline installs the pinned Editor via Unity
   Hub if the image doesn't ship `6000.4.5f1`. That costs ~10 minutes of build time
   whenever it happens.
4. **`NSUserTrackingUsageDescription` is hardcoded English.** Info.plist strings
   localize through `InfoPlist.strings`, not Unity Localization, so this one string
   sits outside the project's localization-key rule. It needs an `InfoPlist.strings`
   per language before a non-English store launch.
5. **`unity/Assets/StreamingAssets/google-services-desktop.json` is committed** and
   carries the Firebase Web API key. Not a secret in the strict sense (see
   `.env.example`), but worth a look if the GitHub repo is public.
