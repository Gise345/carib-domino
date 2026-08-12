# ADR 0013 — Match series (Cut-Throat: Classic & Quick)

- **Status:** Accepted
- **Date:** 2026-08-09
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0006 (N-player online), ADR 0010/0012 (matchmaking), ADR 0011 (auto-start)

## Context

Online play settled each round standalone and offered a manual rematch vote.
Real dominoes plays a *series* to a target, with running totals and a match
winner. Two Cut-Throat formats were requested.

## Decision

**Cut-Throat online/private games are played as a scored series.** A round win is
worth a flat **1000 points**; losers get 0; a draw awards nobody.

- **Classic (six love):** first player to **6000** wins — unlimited rounds.
- **Quick:** a fixed **6 rounds**, most points wins. A tie for the lead keeps the
  match alive for **sudden-death** rounds until someone leads.

Implementation:

- `SeriesState` (pure, tested) tracks cumulative points and decides `IsOver` /
  `Winner`; `MatchFormat` / `MatchFormatRules` hold the numbers. `ApplyRound`
  folds a `MatchOutcome` in.
- `NetworkedMatch` replicates the series (format, per-seat points, `MatchOver`,
  `WinnerSeat`, a `SeriesVersion` bump) so every device shows the same scoreboard.
- **Auto-advance replaces the rematch vote for series:** when a round ends the
  authority folds the result into the series, publishes the scores, and — if the
  match isn't decided — auto-deals the next round after a short beat. When it is
  decided, it flags `MatchOver`.
- The board shows a **scoreboard HUD** (round, target, per-seat totals) and a
  **match-over screen** (winner + final scores → Back to Lobby). Between rounds a
  brief interstitial shows the round result, then play advances.
- The lobby carries a **Classic / Quick** picker; the format is a matchmaking
  property so Classic and Quick pools never cross-match.

**Scope.** Cut-Throat only — points are per player. Partner keeps single-round +
manual rematch (a team-based series is a later extension). Offline practice stays
single-round. Series scoring is client/networked and **casual** — each round
still settles server-side per ADR 0007; match-level payouts arrive with the coin
economy (M6), which will mirror `SeriesState` in TypeScript for authoritative
settlement.

## Consequences

**Positive**
- Cut-Throat is now a real game to a target, with two paces, reusing the round
  engine and the existing advance/settlement plumbing.
- The scoring brain is pure and unit-tested; only the networked wiring is
  device-verified.

**Negative / accepted trade-offs**
- **Casual scoring** — the series total isn't server-authoritative yet (no coins
  ride on it). Closed by M6 (server wallet + TS parity of `SeriesState`).
- **Match-over offers Back to Lobby only** — a "New Match / rematch series" button
  is a small follow-up.
- **Partner has no series yet** — single round + rematch, pending a team-based
  `SeriesState`.

## References

- `unity/Assets/_Project/Scripts/Core/State/SeriesState.cs`, `MatchFormat.cs` — the scoring engine.
- `unity/Assets/_Project/Scripts/Net/NetworkedMatch.cs` — replicated series.
- `unity/Assets/_Project/Scripts/Net/OnlineMatchController.cs` — apply-round + auto-advance.
- `unity/Assets/_Project/Scripts/Game/BoardBootstrap.cs` — scoreboard + match-over.
