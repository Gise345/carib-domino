# ADR 0015 — Pose (previous-winner opening) and Keys (both-ends lock-out)

- **Status:** Accepted
- **Date:** 2026-08-13
- **Scope:** `rules`, `core`, `net`, `functions`
- **Supersedes/relates:** [0007](0007-*.md) (replay-log parity), [0013](0013-match-series.md) (match series)

## Context

Two Jamaican-dominoes rules were missing from the engine, both surfacing only
across a multi-round **series** (0013):

1. **Pose** — who opens a round, and with which tile.
2. **Keys** — a bonus-scoring win condition.

Before this change every round opened the same way: the holder of the highest
double led, and had to play exactly that tile (`StartingPlayerRule.FindLead`).

## Decision

### Pose

- **Round 1 of a brand-new game** → forced open: the highest double leads
  (unchanged behaviour).
- **Rounds 2+** → the **previous round's winner poses** and may open with **any
  tile** (a "free pose").
- **Cut-throat battle rounds** (a lead tie, 0013) → forced open again; the
  double-six poses until one battler wins.
- A round that ended in a **draw** (no single winner) falls back to a forced open.

Represented in the engine by a round-level `MatchState.FreeOpening` flag plus an
`openerIndex` argument to `Dealer.Deal`:

- `openerIndex == -1` (default) → standard rule (`FindLead`), `FreeOpening=false`.
- `openerIndex >= 0` → that seat leads; `FreeOpening` follows the caller.

When `FreeOpening` is set, the rule engine's empty-chain branch enumerates every
tile in the opener's hand as a legal opening placement instead of the single
forced lead tile.

### Keys

A **key** is a domino win (winner empties their hand) where:

1. the winning tile was playable on **both** open ends (a capicúa — the tile
   matched the *other* end too, not just the one it was placed on), **and**
2. **no opponent** still held either of the tile's two pip values (a true
   board lock-out).

A key scores `MatchFormatRules.KeyPoints` (2000) instead of the flat 1000. It is
surfaced on `MatchOutcome.IsKey` and awarded in `SeriesState.ApplyRound`.

## Networking / trust

The opener + free-opening flag are **series context**, not per-round client
input: every client derives them locally in `OnlineMatchController` from state it
already holds (replicated seed, `RoundNumber`, battle mask, and the previous
finished round still in `CurrentState`), so no new networked field was added and
all clients deal identically.

For settlement (the server trust anchor, 0007), `ReplayInput` gains optional
`openerIndex` / `freeOpening`. These must be **server-derived** from the prior
round's winner when series-aware settlement lands (M6) — never trusted from the
client, exactly like `seed` and `mode`. Until then they are inputs to
`replayRound` only.

## Parity

Both rule engines (C# `Pose.Core`, TS `functions/src/rules`) implement identical
`KeyRule` / free-opening logic. The replay-fixture generator
(`scripts/replay-fixtures`) emits `isKey` on every outcome and a spread of
free-opening rounds (every opener seat, both modes), and deterministically
searches for real key games so the Vitest parity suite exercises both — a
regression on either side breaks the build.

## Consequences

- `MatchState`, `Dealer.Deal`, `MatchOutcome`, and `ReplayInput` gained fields —
  all additive with safe defaults, so pre-existing callers are unaffected.
- A "who poses" side popup announces the opener at the start of each series
  round (auto-dismiss 10s). The key "mash up the board" board animation is not
  yet built; the round interstitial announces the key in text for now.
- Series-aware server settlement (deriving opener/free-opening authoritatively)
  is required before wallets depend on replay in M6.
