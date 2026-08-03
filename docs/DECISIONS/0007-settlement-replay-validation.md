# ADR 0007 — Match settlement: server-side replay validation + server-issued seed

- **Status:** Accepted (M4.1 landed; M4.2/M4.3 planned)
- **Date:** 2026-08-03
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Extends:** ADR 0006 (N-player online), `docs/ARCHITECTURE.md` §4 (trust model), §5 (RNG)

## Context

Through M3, the `submitMatchResult` Cloud Function **trusted the client's claimed
outcome** — it took `{outcome, endReason, score}` and incremented `/stats/{uid}`
verbatim. That was always a stopgap (its own comments say so). Before online
results can touch stats/ELO/wallets, the server must stop trusting the client
and compute the result itself.

Two independent attacks matter (see the session discussion):

1. **Outcome lying** — a client claims a win it didn't earn, or inflates its
   score.
2. **Deal stacking** — because the deal is a pure function of the seed, and the
   seed was chosen client-side (`OnlineMatchController.NewSeed` = clock ticks), a
   modified host could search locally for a seed that deals itself a loaded hand.

Replay validation defeats (1); a server-issued seed defeats (2). They are
orthogonal — neither substitutes for the other.

## Decision

Settle matches by **re-deriving the outcome on the server from raw inputs**, and
take seed generation away from the client. Delivered in three slices:

### M4.1 — canonical TypeScript rule engine + parity (landed)

Port the C# rule engine to `functions/src/rules/` (PRNG, tiles, chain, hands,
dealer, `CutThroatRules`, outcome, plus a `replayRound` entry point).
CLAUDE.md principle #2 (rule-engine parity) is enforced by **cross-language
fixtures**: a committed dotnet tool (`scripts/replay-fixtures/`) compiles the
real C# engine and emits `functions/test/fixtures/*.json` — PRNG sequences and
`(seed, players, moves) → outcome` games across 2/3/4 players and every ending
(domino / block / draw / resign). Vitest replays them through the TS engine and
asserts identical results. The C# engine is the reference; regenerate the
fixtures whenever it changes.

### M4.2 — server-issued seed (planned)

A `startMatch` callable rolls a random seed with a proper RNG, stores it in
Firestore under a match id, and returns it. The client never chooses or predicts
the seed. The host distributes the returned seed via the existing Photon
`NetworkedMatch.Seed` field; `OnlineMatchController.NewSeed` / `NextSeedProvider`
(the seam left in ADR 0005/0006) are replaced by the fetched value.

### M4.3 — settlement (planned)

`submitRoundLog` (replacing the trusting `submitMatchResult`) takes the match id
+ the client's move log, looks up **the server's own stored seed** (never the
client's), replays via the M4.1 engine, and writes stats for the verified
caller — rejecting logs whose moves are illegal, out of turn, or don't finish
the round. Because the server owns the seed, the client can lie about neither the
seed nor the outcome. **Offline practice stops writing competitive stats**
entirely (no move log, no integrity guarantee — it's practice).

### Randomness: trusted server RNG now; provably-fair later

The seed is rolled by a trusted server RNG. Players trust the server is honest —
the norm for online card/domino games. A **provably-fair (commit-reveal)** scheme
— publish `hash(seed)` before the deal, reveal `seed` after, so players can verify
the deal wasn't rigged without trusting the server — is deferred as a future
hardening slice. This ADR is the tracked home for that follow-up; code that
issues seeds should reference it rather than carry a bare `// TODO`.

The visible "shuffle in front of the players" animation is a **presentation**
concern (a cosmetic shuffle/deal animation over the server-fixed result), tracked
separately from this settlement/trust work.

## Consequences

**Positive**
- The server never believes a claimed result — it recomputes one. Outcome lying
  and deal stacking are both closed once M4.2/M4.3 land.
- Parity is proven off-device, in CI-friendly unit tests, not eyeballed.
- One seam (server-issued seed) already exists in the netcode from ADR 0005/0006.

**Negative / accepted trade-offs**
- **Two engines to keep in sync.** Any rule change now means C# + TS + regenerated
  fixtures. The parity suite makes drift loud, but it is real duplicated work.
- **No provably-fair verification yet** — players must trust the server RNG until
  the commit-reveal slice.
- **Deploy/infra cost** for M4.2/M4.3 (callables, Firestore match docs, and the
  `carib-domino` IAM/org-policy grants noted in the infra memory).
- **Startup round-trip** — server-issued seed adds one callable before the first
  deal; mitigated by fetching during the fill/waiting window.

## References

- `functions/src/rules/` — the canonical TS engine (M4.1).
- `functions/test/rules/*.parity.test.ts` + `functions/test/fixtures/` — parity suite.
- `scripts/replay-fixtures/` — the C# fixture generator.
- `unity/Assets/_Project/Scripts/Core/` — the reference C# engine.
- ADR [`0005`](0005-in-place-online-rematch.md) / [`0006`](0006-n-player-online.md) — `NextSeedProvider` seam this replaces.
