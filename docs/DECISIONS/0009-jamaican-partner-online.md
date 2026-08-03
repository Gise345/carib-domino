# ADR 0009 — Jamaican Partner as a selectable online mode

- **Status:** Accepted
- **Date:** 2026-08-03
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0003 (team & partnership model), ADR 0006 (N-player online), ADR 0007 (settlement)

## Context

Online play so far shipped a single ruleset — Cut-Throat (every player for
themselves, 2–4 seats, ADR 0006). Jamaican Partner (4 players, 2 fixed teams of
2, partners seated across the table) already existed as a **rule engine** on both
sides (`JamaicanPartnerRules` in C# and TypeScript, with `Partnership.AlternatingPairs`
seating 0 & 2 vs 1 & 3), verified by the shared replay fixtures. What was missing
was the wiring to actually *pick* it and carry the choice through matchmaking,
the networked match, and settlement.

Two properties had to hold:

1. **Engine parity** — the same round replayed through the C# client and the
   TypeScript settlement function must produce the identical team outcome. This
   is already covered by the fixture suite; Partner just needed to be represented
   in the fixtures (52 Partner games added alongside the 144 Cut-Throat ones).
2. **The mode cannot be client-asserted at settlement.** A client that could tell
   `submitRoundLog` "this was Cut-Throat" (or "Partner") after the fact could pick
   whichever scoring favoured it. So the mode must be **recorded server-side at
   match creation** and read back from the trusted record at settlement — never
   from the settlement call's own payload.

## Decision

**Jamaican Partner is a first-class online mode selected in the lobby, recorded
by the server at `startMatch`, and used verbatim by settlement.**

- **Lobby.** *Create Room* reveals a mode picker (Cut-Throat / Partner).
  Cut-Throat then reveals the existing 2/3/4 count picker; **Partner forces 4
  players** and starts immediately. The choice rides `OnlineRoomActive(code,
  count, mode)` into `BoardBootstrap` and on into `OnlineMatchController.Setup`.
- **Networked match.** `NetworkedMatch` gains `[Networked] GameMode GameMode`,
  set by the host at spawn. Joiners read it off the replicated state — mode, like
  seed and player count, comes from the host, not from each client's own UI.
- **Dealing.** `OnlineMatchController.DealCurrentRound` selects the partnership
  and rule engine from the match's `GameMode`: `AlternatingPairs` +
  `JamaicanPartnerRules` for Partner, `CutThroat` + `CutThroatRules` otherwise.
  The same branch runs on the initial deal and every rematch.
- **Server record.** `startMatch` accepts `mode: 'cutthroat' | 'partner'`
  (Zod-validated, `partner` refined to require `playerCount === 4`) and stores it
  on the `matches/{id}` document.
- **Settlement.** `submitRoundLog` reads `mode` **from the match document**, not
  from the client's submission, and drives the replay and `partnershipFor` from
  it. `resultForSeat` is team-based (a seat won iff its team is the winning team),
  which reduces cleanly to Cut-Throat's solo teams.
- **Presentation.** In a team game the round-over text is framed from the local
  player's team ("Your team wins +N" / "Your team loses (−N)"); Cut-Throat keeps
  the individual-winner line. Name-plates are tinted by team (local team gold,
  opposing team blue); Cut-Throat clears back to white.

### Short-start excluded for Partner

The 3+P fill-timeout that offers "start with the players present" (ADR 0006) is
**not armed for Partner**, because Partner needs exactly 4 — a 3-hand Partner deal
is invalid. A Partner room waits until the fourth seat fills.

## Consequences

**Positive**
- A second ruleset ships online with no new networked contract beyond one
  replicated enum, reusing the already-parity-tested Partner engine and the M4.3
  settlement path unchanged in shape.
- The mode is trusted (server-recorded) on the same footing as the seed and
  outcome — a client cannot shop scoring rules after the fact.

**Negative / accepted trade-offs**
- **Seat → uid attribution still trusts the host** (the residual gap ADR 0007
  flags). Team-based settlement inherits it: a dishonest host could still
  misattribute seats. Unchanged by this ADR; blocked on the server-roster work
  before wallets/ELO.
- **Partner is 4-only** — no 2v1 or short-handed Partner. Intended.
- Team colours are assigned from the *local* player's perspective (your team vs
  theirs) rather than fixed per team id, so two devices show the same round with
  swapped colours. This is the intended "my team" framing, not a bug.

## References

- `unity/Assets/_Project/Scripts/Core/State/GameMode.cs` / `functions/src/rules/gameMode.ts` — the mode enum, mirrored.
- `unity/Assets/_Project/Scripts/Net/OnlineMatchController.cs` — `DealCurrentRound` mode branch.
- `functions/src/matchmaking/startMatch.ts` — records `mode`.
- `functions/src/settlement/submitRoundLog.ts` — reads `mode` from the match doc.
- `functions/src/rules/jamaicanPartnerRules.ts` — team scoring (parity with the C# engine).
- ADR [`0003-team-and-partnership-model.md`](0003-team-and-partnership-model.md), [`0007-settlement-replay-validation.md`](0007-settlement-replay-validation.md).
