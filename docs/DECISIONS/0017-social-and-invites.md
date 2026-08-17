# ADR 0017 — Facebook social: leaderboard, profile, capped invite reward (M7)

- **Status:** Accepted (server foundation landed; Facebook client flow + prod deploy pending)
- **Date:** 2026-08-14
- **Scope:** `functions`, `infra`
- **Relates:** [0016](0016-coin-economy-and-roster.md) (wallet), [0007](0007-*.md) (settlement / stats)

## Context

M7 adds Facebook social: sign-in, seeing friends on the leaderboard/ranking,
finding friends to challenge, a player-profile card, and a **250-coin reward per
friend invite sent**. Two realities shape the design:

1. **The Facebook client flow is externally gated.** Login, the friend graph, and
   invite sending require the **Facebook SDK for Unity** + a **Facebook app** with
   App Review — not available in this repo yet. Facebook also only returns friends
   who *also play Pose and granted permission* (`user_friends`), and the classic
   "invite everyone" dialog is deprecated (invites go via App/Game Requests).
2. **Coins are a trust boundary.** Any reward must be server-authoritative and
   abuse-resistant — a client can't be trusted to say "I sent an invite, pay me".

So M7 splits cleanly: the **server** owns leaderboard data, the profile aggregate,
and the reward economy (built + tested now); the **client** owns the FB SDK plumbing
that feeds friend uids + invite ids into those functions (built when the FB app exists).

## Decision (server foundation — this pass)

All additive; the live settlement/wallet paths are untouched.

- **`claimInviteReward`** — mints **250 coins** (`INVITE_REWARD`) per invite, capped
  at **3 rewarded invites per UTC day** (`INVITE_DAILY_CAP`), de-duplicated by a
  unique `inviteId`. The cap/de-dup rules are a pure, unit-tested function
  (`social/invite.ts`); the callable applies them in one transaction (evaluate →
  credit wallet → persist the day's ledger). The ledger lives at
  `inviteRewards/{uid}` (Firestore rules: **deny-all client**, so the cap can't be
  bypassed). Reward chosen "per sent, capped daily" over "on accepted" for
  simplicity and instant gratification; the cap bounds farming to 750 coins/day.

- **`getLeaderboard`** — ranks players by `wins` or `points` (`totalScore`).
  `global` orders all `/stats` by the metric; `friends` ranks the caller + supplied
  Facebook-friend uids. Reads `/stats` via the Admin SDK (bypassing the read-own
  client rule for this aggregate) and joins `/users` for display names.

- **`getProfile`** — one call returning the profile card's fields: name (`/users`),
  coins (`/wallets`), and lifetime stats (`/stats`) with a derived win rate.
  Read-your-own only (uid from auth).

## Trust model

- The invite ledger and reward are writable only by Cloud Functions; the client
  supplies an `inviteId` (a UUID or the FB App-Request id) but cannot set its own
  balance or bypass the daily cap.
- The leaderboard exposes only public-ish stat fields (name, wins, points, games);
  it does not leak wallet balances or private data.
- Friend uids for the `friends` leaderboard are supplied by the client (resolved
  from the FB friend graph). A malicious client could pass arbitrary uids to *view*
  their public stats, which is acceptable (same data the global board shows).

## Status / follow-up

Landed (build + lint + Vitest, non-breaking): the three callables, the pure invite
economy + tests, `inviteRewards` Firestore rule, `index.ts` exports, economy
constants.

**Pending (needs external setup / a later pass):**

1. **Facebook client (needs the FB app + SDK):** login button + Firebase Facebook
   auth provider; resolve the FB friend graph → app uids (feeds `friendUids`); the
   invite-send flow that generates `inviteId`s and calls `claimInviteReward`; the
   "challenge a friend" hook into matchmaking.
2. **Unity profile card + leaderboard UI** consuming `getProfile` / `getLeaderboard`.
3. **Production cutover:** deploy functions + `firestore.rules` to the prod project
   and flip `EnvironmentConfig` to prod (needs the GCP org-policy/IAM grants noted
   in the infra memory). Online-match stats already write to `/stats` (0007), so
   "tracking" is on the moment prod is deployed.
