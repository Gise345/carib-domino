# ADR 0016 — Coin economy, wallet, and server-side series roster (M6)

- **Status:** Accepted (foundation landed; settlement payout wiring pending)
- **Date:** 2026-08-14
- **Scope:** `functions`, `infra`
- **Relates:** [0007](0007-*.md) (replay settlement + trust gaps), [0013](0013-match-series.md) (series), [0015](0015-pose-and-keys.md) (server-derived opener)

## Context

Play is free but staked in coins. The design (recorded in the economy memory):
a **flat 1000-coin entry** per player forms the pot, new players start with
**10,000**, the **match winner takes the pot** plus a **2000 minted key bonus**
per key scored, and losers forfeit their stake. Everything money must be
**server-authoritative** — no client may write a wallet, ELO, or match result
(trust boundary 1).

Two gaps blocked this:

1. **No wallet.** There was nowhere to hold coins.
2. **No trustworthy roster.** Settlement (0007) attributed results to uids using
   the **host's self-reported** `seatUids` — a residual trust gap flagged in 0007.
   And each *round* gets its own `matches/{matchId}` seed doc (a rematch/advance
   calls `startMatch` again), so the server had no concept of a **match/series**
   to debit an entry from or pay a pot to.

## Decision

### Wallet — `wallets/{uid}`

`{ coins }`, server-authoritative. Firestore rules: **read-your-own, no client
write**. Created lazily and funded with `STARTING_COINS` on first touch. All
mutation goes through `functions/src/wallet/wallet.ts` helpers (`readOrInitWallet`
→ `debit` / `credit`) inside a transaction. `getWallet` materialises + returns a
caller's balance for the header display.

### Series + roster — `series/{seriesId}`

A **series** is the server's authoritative record of a whole match:
`{ mode, format, playerCount, roster: {seat→uid}, pot, teamPoints, status }`.
Client access is **fully denied**; the id reaches clients through `openSeries`'s
return value.

- `openSeries` creates the doc (mode, format, seat count).
- `joinSeries({ seriesId, seat })` — each client claims **its own** seat using
  **its own authenticated uid**, and in one transaction debits `ENTRY_STAKE` into
  the pot. This roster — seat→uid proven by each player's own auth, not host
  self-report — is what makes result→uid attribution trustworthy, closing the
  0007 gap. Idempotent per (uid, seat); rejects a taken seat, a double-seat, or
  an underfunded wallet.

Rounds still fetch a per-round seed via `startMatch`; a round is tagged with its
`seriesId` so settlement can find the series it belongs to.

### Economy math — pure and tested

`functions/src/lib/economy.ts` (`potFor`, `splitPayout`, `canAfford`, the
constants) and `functions/src/economy/series.ts` (`seriesTarget`, `accumulate`,
`seriesWinner`) hold every money/scoring rule as pure functions — the server's
mirror of the client `SeriesState`. Unit-tested (26 cases): pot sizing, even
Partner splits with coin-conserving remainders, key bonuses, and the classic/quick
targets.

## Trust model

- Wallets and the series roster are writable **only** by Cloud Functions.
- The winner is **not** taken from a client claim: settlement accumulates each
  **replay-validated** round's team result into `series.teamPoints`
  (`accumulate`), and pays out only when `seriesWinner` reports a team at target.
  This also makes the server the series authority the pose/opener rule needs
  (0015).
- A player can only stake from and read **their own** wallet (auth uid, never
  payload).

## Status / follow-up

Landed this pass (built, linted, unit-tested, non-breaking — the live per-round
`submitRoundLog` stats path is untouched):

- `wallets` + `series` schema and Firestore rules.
- `getWallet`, `openSeries`, `joinSeries` callables.
- Pure economy + series accounting with tests.

**Pending (next pass — modifies the live settlement path, needs emulator
verification before deploy):**

1. Tag each round's `matches/{matchId}` with its `seriesId` (in `startMatch`).
2. In `submitRoundLog`, after the round validates, `accumulate` the team result
   into the series; when `seriesWinner` fires, `splitPayout` the pot (+ key
   bonuses) via `credit` to the winning team's roster uids and mark the series
   `settled` — all in the existing transaction, idempotent.
3. Client wiring: call `openSeries`/`joinSeries` at table formation, pass
   `seriesId` through the round loop, and show `getWallet` coins in the header.
4. Refund/timeout policy for a series that never completes (stuck pot).
