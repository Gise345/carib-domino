# ADR 0003 — Team and Partnership model in the rule engine

- **Status:** Accepted
- **Date:** 2026-05-06
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Supersedes:** none

## Context

M1 step 2 shipped a single-round rule engine for what is colloquially called "Block dominoes" (renamed in M1 step 3 phase 1 to its canonical Jamaican name, **Cut-Throat**). At that point the data model was player-only: `MatchState` carried `IReadOnlyList<PlayerId> Players`, `MatchOutcome` carried a `PlayerId? WinnerId`, and there was no concept of a *team*.

M1 step 3 introduces the second Jamaican variant, **Partner**, which is fundamentally team-based: two pairs of players play across the table from each other, the winning *team* scores rather than the winning player, and several scoring rules (notably the Block-end "lowest individual pip count selects the winning team") only make sense when the engine knows who is partnered with whom. A handful of other variants on the launch roster ([`docs/ARCHITECTURE.md` §8.1](../ARCHITECTURE.md)) — Trinidadian, Cuban, Puerto Rican, Latin/Domino Latino — are also team-based.

The decision here is **where the team concept lives** in the type system, **how Cut-Throat fits into it without a special case**, and **how the winning identity is split** between the player who triggered the win and the team that scores.

## Decision

Introduce three new types in [`unity/Assets/_Project/Scripts/Core/State/`](../../unity/Assets/_Project/Scripts/Core/State/):

| Type | Role |
|---|---|
| `TeamId` | Strongly-typed identifier wrapping a non-empty string. Mirrors `PlayerId` exactly. |
| `Team` | A `TeamId` plus an `IReadOnlyList<PlayerId>` of members. Validates non-empty, no duplicate members. |
| `Partnership` | An `IReadOnlyList<Team>` plus an O(1) `GetTeamOf(PlayerId)` lookup. Validates that no player appears in two teams and that all `TeamId`s are unique. |

Add a `Partnership` field to `MatchState` (alongside `Players`), threaded through the constructor and preserved across `With(...)` calls. Validate at construction that the partnership covers exactly the same set of players as the match — no extras, no missing — so `GetTeamOf(currentPlayer)` is always safe inside the rule engine.

Add a nullable `TeamId? WinningTeamId` field to `MatchOutcome`, set on every non-draw win (paired with the existing `PlayerId? WinnerId`) and `null` on draws (paired with a `null` `WinnerId`).

Two factories on `Partnership`:

- `Partnership.CutThroat(IReadOnlyList<PlayerId>)` — each player is a solo team. Team IDs are deterministically named `"team:{playerValue}"`. **Cut-Throat is unified into the team model rather than special-cased.**
- `Partnership.AlternatingPairs(p1, p2, p3, p4)` — exactly four players, positions 0+2 form `team_a`, positions 1+3 form `team_b`. Mirrors physical seating where partners sit across the table.

`Dealer.Deal` accepts a `Partnership` parameter and propagates it onto the new `MatchState`. `CutThroatRules.GetOutcome` populates `WinningTeamId` by calling `state.Partnership.GetTeamOf(winnerId)` — the same call site that the upcoming `JamaicanPartnerRules` (phase 3) will use, just with a different partnership shape feeding it.

## Rationale

### Teams live on `MatchState`, not inside the rule engine

Two reasonable alternatives existed:

1. **`Partnership` is constructor-private to each rule engine** (e.g. passed to `JamaicanPartnerRules` at construction; `CutThroatRules` doesn't know about teams).
2. **`Partnership` is a first-class field on `MatchState`** alongside `Players`.

Choice (2) is correct because:

- `MatchState` is what gets serialized into the match log and replayed by the server-side validator (per [`docs/ARCHITECTURE.md` §5](../ARCHITECTURE.md)). For the validator to verify a partner-game outcome, it must know the partnership; the partnership is an integral property of "what game was played", not an implementation detail of one rule engine. Putting it on the state guarantees it travels with the log.
- Replay determinism — the foundational invariant for the M4 settlement validator — requires that `Dealer.Deal(seed, players, partnership, …)` be a pure function. The partnership has to be an *input* to the deal (and thus to the state), not a side-channel.
- A future spectator UI ("show me the teams on this game") wants to read `state.Partnership` directly without coupling to a specific rule-engine class.

### Cut-Throat is unified, not special-cased

Cut-Throat could have been modelled as "no partnership; `WinningTeamId` is always null." That would split the engine in two: partner variants resolve a winning team, cut-throat does not. Every consumer of `MatchOutcome` (UI, settlement, leaderboards) would then need to branch on which variant produced the outcome.

Modelling cut-throat as **each player on their own one-member team** unifies the two paths: every rule engine populates `WinningTeamId` the same way (`partnership.GetTeamOf(winnerId)`), and downstream code never branches on variant. The team-of-one is a real team — its ID is deterministic, it's queryable, and its score is the player's score. The cost is one cheap factory and one trivial dictionary entry per player.

This is the same trick the C# language uses with empty `IEnumerable<T>` rather than `null` — uniformity is worth the trivial wrapper.

### `WinnerId` (player) and `WinningTeamId` (team) are both kept

A defensible alternative was to drop `WinnerId` entirely once teams existed: the team is the unit that scores, so why keep the player?

`WinnerId` is still valuable because it's the *triggering player*: in a Domino end it's the player who emptied their hand; in a Block end it's the player whose individual pip count was lowest. The triggering player matters for analytics ("who closed the round"), for replays ("animate the winning tile being placed by Alice"), and for some variants' bookkeeping ("did the player score with their last tile or their second-to-last"). The `WinningTeamId` is what the wallet/leaderboard layer awards points to; the `WinnerId` is the human-readable cause.

In Cut-Throat the two are paired by definition (the team has one member). In Partner they typically diverge in size (the team has two members, the winner is one of them).

### Strict partnership-must-cover-players validation in `MatchState`

`MatchState`'s constructor enforces that the partnership contains *exactly* the same set of players as the match. Two looser alternatives were considered:

- **Validate only that every player has a team** (allow extras in the partnership). Rejected because an "extra" player in a partnership is almost certainly a bug — somebody passed the wrong partnership object.
- **Don't validate; let `GetTeamOf` throw at use time.** Rejected because by the time `GetTeamOf` throws, the engine has already half-applied a move and the state is harder to reason about. Failing fast at construction localises the error.

The strict invariant means rule engines can call `state.Partnership.GetTeamOf(state.CurrentPlayer)` with no defensive check.

## Consequences

**Positive**

- One uniform code path for outcome resolution: every rule engine, present and future (Trinidadian, Cuban, Puerto Rican, Latin), populates `WinningTeamId` the same way. No variant-specific branches in downstream code.
- The match log replays cleanly: the partnership is in the state, so the server-side validator sees what the client saw with no ambient assumptions.
- The `Partnership` type is small enough (an `IReadOnlyList<Team>` and a private dictionary) to be cheap to construct and equality-compare; no need for interning or shared instances.
- Scope-limited: `JamaicanPartnerRules` (phase 3) needs only to enforce variant-specific constraints (4 players, an `AlternatingPairs` partnership, partner-aware Block scoring) and to re-use the same `WinningTeamId` resolution call. The plumbing is already in place after this ADR.

**Negative / accepted trade-offs**

- **Every existing `MatchState` constructor call site has to pass a `Partnership`.** Mitigated by the two factories: `Partnership.CutThroat(players)` is one extra line at every test fixture and at the single `Dealer.Deal` callsite. The cost is one-time and small.
- **Cut-Throat now allocates N + 1 small objects on every deal** (N teams + 1 partnership). Trivial in absolute terms (a few dozen bytes per round) and dwarfed by the existing tile-shuffle allocation. Not a hot path.
- **`TeamId` is yet another wrapper struct.** It's the price of strong typing — the same price `PlayerId` already pays. Worth it because the `WinnerId` / `WinningTeamId` split would be confusing without distinct types (both would otherwise be `string`).
- **The `MatchState.With(...)` API doesn't expose `Partnership` as a mutable field.** Intentional: the partnership is fixed for the lifetime of a single round. Multi-round scoring (Six-Love et al.) builds new `MatchState` instances at the start of each round; the partnership for the next round is a higher-layer decision that doesn't belong in the per-round `With(...)` API.

## Alternatives considered

- **`Partnership` is private to `JamaicanPartnerRules`; Cut-Throat doesn't know about teams.** Rejected because it splits the outcome-resolution path between variants and means downstream consumers have to branch on rule type. Also breaks the replay-validator's contract (the partnership has to be in the log).
- **No `Partnership` type; teams are a `Dictionary<PlayerId, TeamId>` carried directly on `MatchState`.** Rejected because the dictionary loses team-level metadata (the `Members` list, the team's `Id`) and the validation logic becomes scattered across rule engines. Wrapping in `Partnership` localises the invariants.
- **`WinningTeamId` only; drop `WinnerId`.** Rejected because the triggering-player information is genuinely useful for analytics, replay rendering, and per-variant bookkeeping. Keeping both costs nothing material.
- **Synthetic `TeamId` values everywhere (e.g. GUIDs).** Rejected because deterministic, debuggable IDs (`"team:alice"`, `"team_a"`, `"team_b"`) make logs and replay diffs much easier to read for a solo developer. Determinism also matters for replay determinism — a GUID-based scheme would have to seed the GUIDs from the match seed, which adds complexity for no benefit.

## References

- [`unity/Assets/_Project/Scripts/Core/State/TeamId.cs`](../../unity/Assets/_Project/Scripts/Core/State/TeamId.cs), [`Team.cs`](../../unity/Assets/_Project/Scripts/Core/State/Team.cs), [`Partnership.cs`](../../unity/Assets/_Project/Scripts/Core/State/Partnership.cs) — the new types this ADR introduces.
- [`unity/Assets/_Project/Scripts/Core/State/MatchState.cs`](../../unity/Assets/_Project/Scripts/Core/State/MatchState.cs) — partnership-aware state with the integrity invariant.
- [`unity/Assets/_Project/Scripts/Core/State/MatchOutcome.cs`](../../unity/Assets/_Project/Scripts/Core/State/MatchOutcome.cs) — `WinningTeamId` field.
- [`docs/ARCHITECTURE.md` §5](../ARCHITECTURE.md), [§7.2](../ARCHITECTURE.md), [§8.1](../ARCHITECTURE.md) — match lifecycle, rule-engine parity, variant catalogue.
- [`docs/SESSION_NOTES/2026-05-04-handoff-m1-step-3.md`](../SESSION_NOTES/2026-05-04-handoff-m1-step-3.md) — the planning document that drove this phase.
- ADR [`0001-tech-stack.md`](0001-tech-stack.md) — establishes the dual rule-engine parity requirement that motivates putting `Partnership` on `MatchState`.
