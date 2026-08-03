# ADR 0008 — Mid-round disconnect is a resign

- **Status:** Accepted
- **Date:** 2026-08-03
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0006 (N-player online), ADR 0007 (settlement)

## Context

Until now a mid-round leave (rage-quit, app kill, lost connection) only showed
the remaining players an "opponent left → back to lobby" overlay. The round was
abandoned with **no recorded result** — the player who was about to win got
nothing, and nothing settled. `OnlineMatchController` already detected the leave
(a drop in `Runner.ActivePlayers`); it just didn't do anything with the round.

The rule engine already models a forfeit (`ResignMove`, and `GetOutcome`'s resign
branch: 2P the other player wins; 3+P the lowest-pip non-resigner wins), and M4.3
already settles any finished round. So the pieces to turn a leave into a real,
settled outcome were all present.

## Decision

**A mid-round leave is treated as a resign by the departed player.**

When `OnlineMatchController` detects that a seat's `PlayerRef` is no longer in
`Runner.ActivePlayers`, the **host** (state authority) submits a
`ResignMove` for that seat via the normal `RPC_SubmitMove` path. That ends the
round through the existing move log → round-over → M4.3 settlement flow, so the
remaining player(s) get the win recorded. One resign ends the round; the engine
decides the winner.

The remaining players still see the "opponent left" overlay (rematch is never
offered — a departed opponent can't play on), but it now leads with the actual
outcome ("Round over — Resigned, X wins +N") when the leave ended the round.

### Deferred: host migration

If the **host itself** leaves, the remaining clients are not the state authority,
so they cannot drive the resign or submit settlement (M4.3's `submitRoundLog` is
bound to the host's uid). Those rounds keep the old behaviour — "opponent left →
back to lobby", unsettled. Seamless host-departure handling (Fusion shared-mode
authority migration + a settlement path that doesn't require the original host)
is its own slice, to be picked up with the server-roster work ADR 0007 already
flags as a prerequisite for wallets/ELO.

## Consequences

**Positive**
- A rage-quit now records the win instead of denying it — the common,
  most-annoying disconnect case is handled, reusing the engine's resign + M4.3
  settlement with no new networked contract.
- 3+P leaves resolve to a defined outcome (lowest-pip non-resigner wins) rather
  than aborting.

**Negative / accepted trade-offs**
- **A host leaving still abandons the round unsettled** — deferred to a host-
  migration slice.
- **3+P play does not continue after a leave** — one resign ends the whole round.
  "Play on without the leaver" is a larger, separate feature.
- Detection is by `ActivePlayers` count, which can't distinguish a clean
  back-to-lobby from a crash — both become a resign, which is the desired result
  either way.

## References

- `unity/Assets/_Project/Scripts/Net/OnlineMatchController.cs` — `ResignDepartedPlayer`.
- `unity/Assets/_Project/Scripts/Core/Rules/CutThroatRules.cs` — resign outcome.
- ADR [`0007-settlement-replay-validation.md`](0007-settlement-replay-validation.md) — the settlement path a resigned round flows into, and the host-binding limitation.
