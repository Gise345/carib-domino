# ADR 0019 — Facebook identity index & login foundation (M7 phase 1)

- **Status:** Accepted (server foundation landed; Unity auth/login UI + prod deploy pending)
- **Date:** 2026-08-17
- **Scope:** `functions`, `infra`, `net`, `ui`
- **Relates:** [0017-social-and-invites](0017-social-and-invites.md) (leaderboard/profile/invite), [0016](0016-coin-economy-and-roster.md) (wallet)

## Context

ADR 0017 built the M7 server economy (leaderboard, profile aggregate, capped
invite reward) but deferred the actual Facebook client flow. Phase 1 lands the
piece everything else depends on: **players signing in**, and their **Facebook
identity being recorded so friends can find them**.

The product ask: a **login screen** offering *Continue with Facebook*, *Sign in
with Email*, or *Continue as Guest*; and, from the profile, *Disconnect Facebook*
and *Log out*. Facebook friends must show on the leaderboard and be findable to
challenge — which requires mapping a friend's **Facebook user id → app uid**.

Two constraints shape the design:

1. **The client is untrusted for identity.** A client could claim any Facebook id.
   The only trustworthy source of "this account owns Facebook id X" is the
   **signed Firebase auth token**: after a Facebook link/sign-in, Firebase Auth
   verifies the Facebook credential and stamps the fbId into
   `token.firebase.identities['facebook.com']`. We read it there, never from the
   request payload.
2. **The friend graph is sensitive.** The fbId → uid map must not be
   client-readable (scraping who-plays-what) nor client-writable (spoofing a
   friend link). It lives in a **server-only** collection.

## Decision

### Auth model: link, don't replace

Guest play uses Firebase **anonymous** auth. *Continue with Facebook* (or *Sign in
with Email*) **links** the new credential onto the current anonymous user, so the
player keeps their uid, coins, and stats. If the Facebook credential already
belongs to another Firebase user (`credential-already-in-use`), the client falls
back to **signing into that existing account** rather than creating a duplicate.
Email is **password-based** (sign-up + sign-in + password-reset email), not
passwordless email-link (which would need deep-link infrastructure).

### `/facebookIndex/{facebookId}` — server-only fbId → uid map

New Firestore collection, **`allow read, write: if false`** (Admin SDK only).
Maps a Facebook user id to the app uid that owns it. This is the authoritative
source for friend resolution; the copy of the name/photo on the client-writable
`/users` doc is display-only and never trusted for identity.

### `syncFacebookIdentity` callable

Called by the client right after a successful Facebook link. It:

1. Reads the **verified** fbId from `request.auth.token.firebase.identities`
   (`failed-precondition` if none is linked).
2. In one transaction: rejects with `already-exists` if that fbId is indexed to a
   **different** uid (belongs to another player — no hijack); otherwise writes
   `/facebookIndex/{fbId} → { uid }` and mirrors the Facebook **display name +
   photo** onto `/users/{uid}` (merge) so leaderboards/profile show a real name.

Token parsing is factored into a pure, unit-tested helper
(`social/facebookIdentity.ts`: `facebookIdFromToken`, `facebookProfileFromToken`)
so the extraction/shaping rules are verified without a live token.

### Client (Unity) — this phase's non-server work

- `Net/AuthService.cs` — one API over Firebase Auth: guest / email (sign-up,
  sign-in, reset) / Facebook (link, or sign-in on collision) / unlink / sign-out.
- `Net/FacebookAuthService.cs` — isolates the Facebook SDK (`FB.Init` →
  `LogInWithReadPermissions(public_profile, user_friends)` → access token).
- `FirebaseBootstrap.cs` — init only; **no auto-anonymous sign-in** (guest is now
  an explicit choice). Returning users skip login (Firebase persists the session).
- `UI/LoginView.cs`, `UI/ProfileAccountView.cs` — the login screen + profile
  connect/disconnect/logout. All strings localized.

## Consequences

- **Positive:** Friend resolution (phase 2) is a trustworthy index lookup, not a
  client claim. Guest→Facebook keeps progress. The FB app-secret stays out of the
  repo/client entirely (Firebase Console only) — the client only ever handles the
  short-lived access token.
- **Cost:** One more server-only collection and callable to operate. The Unity
  auth layer is a real chunk of UI + Firebase Auth wiring, verified on device
  (the FB/Firebase SDKs can't run in headless CI).
- **No rule-engine change**, so no C#↔TS replay-fixture parity work this phase.
- **Deferred:** `resolveFacebookFriends` + friends leaderboard (phase 2), invite
  send → `claimInviteReward` (phase 3), profile-card UI (phase 4), challenge-a-
  friend match flow (phase 5).
