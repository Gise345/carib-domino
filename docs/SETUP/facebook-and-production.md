# Setup you own: Facebook + Production

Two things gate M7 (social) and going live. They need your accounts/credentials, so
I can't do them from here — but everything downstream of them is built and waiting.
Work top to bottom; hand me the **bold "give me"** items at the end of each part.

---

## Part A — Facebook (login · friends · invites)

### A1. Create the Facebook app
1. Go to <https://developers.facebook.com> → log in → **My Apps → Create App**.
2. Use case: **Authenticate and request data from users with Facebook Login** (a
   "Consumer"/"Other"-style app). Name it **Pose Caribbean Dominoes**.
3. In **App settings → Basic**, copy the **App ID** and **App Secret**, and set a
   Privacy Policy URL (Facebook requires one before it leaves dev mode — the
   marketing site can host it).

### A2. Add Facebook Login + the Android platform
1. Left nav → **Add Product → Facebook Login** → Settings.
2. **App settings → Basic → Add Platform → Android**:
   - **Package name** — from Unity: `Edit ▸ Project Settings ▸ Player ▸ Android ▸ Other Settings ▸ Package Name` (e.g. `com.invovibe.posedominoes`).
   - **Default Activity Class Name** — `com.unity3d.player.UnityPlayerActivity`.
   - **Key hashes** — generate from your signing keys (Git Bash):
     ```bash
     # Debug key (password: android)
     keytool -exportcert -alias androiddebugkey \
       -keystore "$USERPROFILE/.android/debug.keystore" \
       | openssl sha1 -binary | openssl base64
     # Release key (use your real keystore + alias)
     keytool -exportcert -alias <your-alias> -keystore <path-to-release.keystore> \
       | openssl sha1 -binary | openssl base64
     ```
     Paste **both** hashes into the Android platform's *Key Hashes* field.
   - (Add the **iOS** platform later with the bundle id when you build for iOS.)

### A3. Enable Facebook in Firebase Auth
1. Firebase Console (**carib-domino**) → **Authentication → Sign-in method → Facebook → Enable**.
2. Paste the **App ID** + **App Secret**.
3. Copy the **OAuth redirect URI** Firebase shows, and in the FB app under
   **Facebook Login → Settings**, paste it into **Valid OAuth Redirect URIs**.

### A4. Import the Facebook SDK for Unity  *(this is the new dependency I flagged)*
1. Download the **Facebook SDK for Unity** (latest) from
   <https://developers.facebook.com/docs/unity/downloads> and import the
   `.unitypackage` into the project.
2. Unity menu **Facebook ▸ Edit Settings**: paste the **App ID**, **App Name**, and
   **Client Token** (FB app → Settings → Advanced → Client Token). This wires the
   AndroidManifest/Info.plist entries automatically.
3. Commit the imported SDK + the generated `FacebookSettings.asset`.

### A5. Permissions (App Review)
- `public_profile` + `email` — available immediately (Standard Access).
- **`user_friends`** — needed to show which friends play Pose on the leaderboard and
  to find them to challenge. It requires **App Review**; submit it with a short
  screencast of the flow. It only returns friends who **also play Pose and granted
  the permission** (not your whole friend list) — by design.
- **Invites** use the **App/Game Requests** dialog (the classic "invite all" dialog
  is deprecated). Set the app category appropriately; a review may be required.

> **Give me:** the **App ID**, and confirmation that (a) the SDK is imported, (b) the
> Firebase Facebook provider is enabled. Then I wire the login button → Firebase
> Facebook credential → friend resolution → the `getLeaderboard` / `claimInviteReward`
> functions that already exist.

---

## Part B — Production cutover (go live + keep stats)

Stats already write server-side for online matches (ADR 0007), so "tracking" turns
on the instant prod is deployed — there's no new tracking code to write.

### B1. Enable APIs + IAM (the infra step from our notes)
Cloud Functions v2 on **carib-domino** needs these (once):
```bash
gcloud config set project carib-domino
gcloud services enable \
  cloudfunctions.googleapis.com run.googleapis.com cloudbuild.googleapis.com \
  artifactregistry.googleapis.com eventarc.googleapis.com
```
If your org blocks public callables (`iam.allowedPolicyMemberDomains`), an **org
admin** must allow an exception for this project so clients can invoke the callables.

### B2. Deploy functions + rules
```bash
cd functions
npm ci && npm run build
firebase deploy --only functions --project carib-domino
firebase deploy --only firestore:rules,firestore:indexes --project carib-domino
```
This ships: `startMatch`, `submitRoundLog`, `getWallet`, `openSeries`, `joinSeries`,
`getLeaderboard`, `getProfile`, `claimInviteReward`, plus the wallet/series/
inviteRewards security rules.

### B3. Make the callables invokable by signed-in clients (v2)
For each callable, grant the invoker binding (region = wherever it deployed, default
`us-central1`):
```bash
for fn in startMatch submitRoundLog getWallet openSeries joinSeries \
          getLeaderboard getProfile claimInviteReward; do
  gcloud run services add-iam-policy-binding "$fn" \
    --region=us-central1 --member=allUsers --role=roles/run.invoker \
    --project carib-domino
done
```
(Callables still check `request.auth` inside — `allUsers` only lets the request
reach the function; unauthenticated calls are rejected in code.)

### B4. Flip the client to prod
- `unity/Assets/_Project/Settings/EnvironmentConfig.asset` → set environment to
  **prod** (switches the Firebase project + Photon AppID).
- Confirm `google-services.json` (Android) / `GoogleService-Info.plist` (iOS) are the
  **prod** project's files.

### B5. Turn on crash + usage tracking
- Add/enable **Crashlytics** and **Analytics** in the Firebase Unity SDK (they're in
  the tech stack). This is the "keep track" piece beyond gameplay stats.

### B6. Verify
1. Fresh install → sign in → play one online match to completion.
2. Confirm in Firestore: `/stats/{uid}` incremented, `/wallets/{uid}` debited/credited,
   and the settlement function logs `submitRoundLog processed … settled:true`.
3. Call `getLeaderboard` / `getProfile` (or open the screens once I build them) and
   confirm real data comes back.

> **Give me:** confirmation prod is deployed (or paste any deploy/IAM errors), and I'll
> verify the client wiring + build the profile card and leaderboard UI against it.

---

## Quick status of what's already built (so you know what's waiting)
- **Server:** wallet, series roster, entry/pot economy, leaderboard, profile
  aggregate, capped invite reward — all deployable now (ADR 0016, 0017).
- **Needs your Part A:** the Facebook login/friends/invite client wiring.
- **Needs your Part B:** everything to actually run against real users + track them.
