# ADR 0022 — Admin console & moderation (security model + plan)

- **Status:** Accepted (Phase A — security spine — landed; dashboard + features pending)
- **Date:** 2026-08-19
- **Scope:** `functions`, `infra`, `web`, `net`
- **Relates:** [0016](0016-coin-economy-and-roster.md) (server-authoritative economy), [0019](0019-facebook-identity-and-friends.md) (social), [0021](0021-ota-updates.md) (OTA)

## Context

We need an admin surface to see analytics, search users, ban terms-violators, and
run promotions. The hard requirement: **admin access must not be forgeable.** A
game client is fully attacker-controlled, so admin authority can never rest on a
client-side check, a hardcoded email in the app, or a Firestore flag a client could
read/write.

## Decision

### Authorization: unforgeable custom claims + a server-only allowlist

- Admin is an **`admin: true` Firebase Auth custom claim**, set by the Admin SDK and
  embedded in the **Google-signed** ID token. A client cannot mint or edit it.
- The **allowlist of admin emails lives only in Cloud Functions** (`admin/admins.ts`),
  never shipped to a client.
- `syncAdminClaim` (callable) grants the caller their own claim **iff** their
  **verified** token email is allowlisted, and strips a stale claim otherwise. Email
  comes from the signed token (`email` + `email_verified`) — unspoofable.
- **Defence in depth:** `assertAdmin` (used by every admin callable) requires the
  claim **and** re-checks the live allowlist, so a stale/forged claim is useless
  unless the email is still listed. The allowlist is the ultimate source of truth.

### Actions are server-only, audited

- Every mutating admin action (ban, promo, claim change) runs in a Cloud Function
  (Admin SDK) and writes an **immutable `/adminAudit`** record (who/what/when/target).
- Admin Firestore collections (`/adminAudit`, `/bans`, `/promotions`, `/adminStats`)
  are **deny-all to clients**; even admin *reads* go through callables, so the whole
  moderation/economy surface stays off the device.
- Bans are enforced **server-side** — gameplay functions reject a banned uid; the
  game shows "account suspended". UI hiding is cosmetic.

### Surface: a web dashboard + a minimal in-game shortcut

- Primary surface is a **separate web admin dashboard** (Firebase Hosting + Google
  sign-in, gated by the claim) — OTA by nature (ADR 0021), best for analytics/search/
  moderation, and keeps admin code out of the player binary.
- A **minimal in-game shortcut** (e.g. quick-ban from a match) is added later for
  convenience; it calls the same guarded functions, so it grants no extra power.

### Why not the alternatives

- **Email check in the client / Firestore `isAdmin` flag** — both spoofable. Rejected.
- **In-game-only admin** — ships admin UI in the player binary, clunky for tables,
  needs an app rebuild per change. Rejected as the primary.

## Phases

- **A (this ADR):** `admin/admins.ts` allowlist, `assertAdmin` guard, `writeAudit`,
  `syncAdminClaim` callable, admin Firestore rules, unit test. Security spine, no UI.
- **B:** web dashboard shell + Google sign-in + claim gate, hosted (Firebase `admin` site).
- **C:** analytics (user count, active, matches, coins in circulation) + user search/detail.
- **D:** ban/unban + enforcement across gameplay functions + game "suspended" screen +
  in-game quick-ban shortcut.
- **E:** promotions (bonus coins / events / promo codes) — server-authoritative, with
  expiry + caps, applied via functions.

## Consequences

- **Positive:** Admin authority is cryptographically gated and re-checked; every action
  is audited; the surface is OTA (web) so it evolves without app builds.
- **Cost:** A second web app + admin function surface to operate. The realistic residual
  risk is an admin's **own Google account** — mitigate with 2FA on those accounts.
- **Note:** the admin allowlist is code — adding/removing an admin is a one-line change +
  functions deploy (deliberate, auditable), not a console toggle.
