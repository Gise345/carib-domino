# ADR 0023 — In-match chat, guest entitlements & chat moderation

- **Status:** Accepted
- **Date:** 2026-08-22
- **Scope:** `functions`, `net`, `ui`, `infra`, `web`
- **Relates:** [0019](0019-facebook-identity-and-friends.md) (identity), [0021](0021-ota-updates.md) (OTA),
  [0022](0022-admin-and-moderation.md) (admin security spine, bans, audit)

## Context

The board HUD shipped a *mock* chat panel — a small corner card with four hardcoded
messages and a label posing as a text field. Nothing was wired. We now need real
in-match chat, and chat is the single highest-risk surface in a free-to-play social
game: it is the vector for harassment, grooming, scams and store-rating damage. It
therefore cannot ship without moderation, and it cannot ship open to throwaway
identities.

A **guest** (Firebase anonymous auth) is an unaccountable identity: banning one costs
the abuser a single tap to replace. Chat — and the voice feature that follows it —
must be tied to an identity with a real cost of replacement.

## Decision

### 1. Chat is server-written, client-read

- Clients **never write** `/chatRooms/**`. Sending goes through the
  `sendChatMessage` callable, which stamps `senderUid` from the **signed token**
  and enforces every gate server-side: ban, guest, membership, mute, rate limit,
  length, profanity.
- Clients **read** their room's messages with a Firestore **snapshot listener**, so
  delivery is realtime and free of polling. `firestore.rules` allow that read only
  for a uid listed in the room's `members` map.
- Rationale: the write path is the attack surface (spoofed sender, flooding, filter
  bypass) and the read path is not. This keeps latency low where it matters while
  leaving nothing about a message client-trusted, per the trust-boundary rule in
  `CLAUDE.md`.

### 2. Rooms are joined by self-claim, never by a host-supplied roster

`joinChatRoom({ roomId })` adds **the caller's own authenticated uid** to the room —
the same seat-claim pattern as `joinSeries` (ADR 0016). A client cannot add, name,
or impersonate anyone else. Rooms cap at `MAX_ROOM_MEMBERS` (4). `roomId` is the
Photon session name, so one chat room spans every round of a series.

### 3. Guests may read chat; they may not send, and they may not use voice

- Enforced server-side from `token.firebase.sign_in_provider === 'anonymous'` —
  unforgeable, unlike a client-side flag.
- The client shows guests the conversation with a locked composer and a
  *Create an account* CTA, and a one-time popup on guest sign-in listing what is
  limited. UI locking is **cosmetic**; the server is the gate.
- Voice/microphone (a later milestone) inherits the same entitlement check, so the
  policy is defined once, here.

### 4. Profanity is masked, not blocked — and the original is kept

The filter replaces matches with `****` and **delivers** the message, while storing
the unmasked original in a **deny-all `originals` subcollection** — Firestore rules are
document-level, so a "server-only field" on a client-readable message document is a
contradiction; the verbatim text needs its own document. Blocking
teaches abusers to probe the filter and destroys the evidence trail; masking keeps
the room civil, keeps the offender unaware they are being logged, and gives a
moderator the verbatim text. Filtered messages are flagged for proactive review.

### 5. Reporting freezes evidence

`reportChatMessage` copies a **frozen transcript** (up to `REPORT_TRANSCRIPT_LIMIT`
messages of that room, unmasked) into an immutable `/chatReports` doc, together with
the room's members, mode, and the server-issued match ids played. Evidence therefore
survives message redaction, room expiry, and account deletion. Reports are
deny-all to clients; admins read them through `assertAdmin`-gated callables.

### 6. Retention: 30 days, indefinite once reported

Rooms and messages carry `expiresAt` (+30d, bumped on each message) and are removed
by a **Firestore TTL policy**. A report clears `expiresAt` on its room and sets
`retained: true`, so anything under moderation is never swept. Bounded storage,
bounded personal-data exposure, no loss of evidence.

### 7. Moderation lives in the existing admin console

New tab in the ADR 0022 dashboard: report queue → full transcript → act. Actions are
`muteUser` (time-boxed chat suspension via `/chatMutes/{uid}`), the existing
`banUser`, and `redactChatMessage`. Every action is `assertAdmin`-gated and written
to `/adminAudit`, reusing the phase-A spine unchanged.

### 8. Refusal codes ride on the message, not in `details`

Unity's `FunctionsException` exposes only `ErrorCode` and `Message` — a callable's
structured `details` payload never reaches the game client. A client that must tell
a mute from a rate limit from a guest lock therefore reads a **stable code prefix**
off the message (`"muted: You are muted in chat."`). Matching the human sentence
instead would break the first time someone reworded it. The two halves of the
contract are `functions/src/chat/refusals.ts` and `Pose.Core.Chat.ChatRefusal`, each
with tests asserting the same three codes.

## Data model

| Path | Client access | Notes |
|---|---|---|
| `/chatRooms/{roomId}` | read: members only | `members` map uid → {name, seat}, `mode`, `matchIds`, `expiresAt` |
| `/chatRooms/{roomId}/messages/{msgId}` | read: room members | `senderUid`, `senderName`, `text` (masked), `filtered`, `severe`, `redacted`, `createdAt`, `expiresAt` |
| `/chatRooms/{roomId}/originals/{msgId}` | none | verbatim text behind a mask — written only when the filter fired |
| `/chatRateLimits/{uid}` | none | the sender's sliding send window |
| `/chatReports/{reportId}` | none | frozen transcript + room meta + reporter/reported uids + `status` |
| `/chatMutes/{uid}` | none | `until`, `reason`, actor — checked by `sendChatMessage` |

## Consequences

- **Positive:** No client-trusted chat field; guests cannot harass; evidence is
  immutable and admin-reviewable; retention is bounded; the voice entitlement rule is
  already decided. The moderation surface is web/OTA (ADR 0021).
- **Cost:** One callable invocation per message (negligible at ≤4 senders per room)
  and ~150 ms send latency versus a direct write. A profanity list is maintenance
  that will always trail slang; reports are the backstop, which is why they freeze
  evidence rather than depend on the filter.
- **Infra:** the TTL policies on `expiresAt` (`chatRooms`, `messages`, `originals`)
  are declared as `fieldOverrides` in `firestore.indexes.json`, so they deploy with
  `firebase deploy --only firestore:indexes` rather than being a console click.
- **Known residual risk:** a room is joinable by anyone who knows its id (the Photon
  session name). For a friend table that is a 6-character code from a 32-character
  alphabet — ~1.07 billion combinations, each guess costing a callable round trip —
  and the room still caps at four members who are listed by name in the panel, so a
  lurker would have to take a seat before the players do and would be visible in the
  roster. Accepted rather than adding a capability check; revisit if room codes ever
  get shorter or rooms ever outlive their match.
- **Deliberately deferred:** message edit/delete by the sender, DMs outside a match,
  image/sticker messages, and automated ML classification. Each widens the abuse
  surface and needs its own decision.
