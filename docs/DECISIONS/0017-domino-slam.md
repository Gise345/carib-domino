# ADR 0017 — Domino slam (consumable, earned by ad or purchase)

- **Status:** Proposed — not scheduled. Recorded so the hook points are known
  before the turn-timer and shop slices are built around them.
- **Date:** 2026-08-15
- **Scope:** `ui`, `core`, `net`, `functions`
- **Relates:** [0016](0016-coin-economy-and-roster.md) (coin economy, wallet
  trust boundary), [0007](0007-settlement-replay-validation.md) (replay parity)

## Context

Slamming the tile down is the signature gesture of Caribbean dominoes — the
physical table shake is most of the fun. In-game it becomes an expressive,
monetisable flourish: the player triggers an exaggerated slam animation, screen
shake, haptic thump and sound sting when placing a tile.

Slams are a **consumable**, not an unlock. A player earns them by watching a
rewarded ad or buys them in the shop, and burns one per use.

## Decision (proposed)

**Cosmetic only.** A slam must never change the move, its legality, its timing
for the turn timer, or anything the round log records. This keeps it entirely
outside the replay-validation path — a slammed placement and a tapped
placement replay identically server-side.

**Balance is server-authoritative.** Slam count is a consumable balance, not a
PlayerPrefs integer. It lives beside the wallet under the same trust rule:
client reads own, only Cloud Functions write. Granting on ad-completion goes
through the ad network's server-side reward callback, never a client claim.

**Cost model** (to be set when the shop lands): rewarded ad → 1 slam;
purchasable in coin bundles; VIP/subscription grants a daily allotment.

## Hook points this creates

- `TileView` / `HandView` — a slam variant of the placement animation, gated on
  balance > 0. Long-press or a dedicated slam button as the trigger (TBD in
  playtest).
- Turn timer (upcoming slice) — the 30s auto-play path never slams.
- Shop (upcoming slice) — a consumables section alongside coins.
- Settings — slam intensity should respect the vibration and sound toggles.

## Consequences

- Nothing to build now. The turn-timer and shop slices should leave room for a
  consumables balance rather than assuming coins are the only server-held
  quantity.
- Revisit and move to Accepted when the shop and ad mediation are configured.
