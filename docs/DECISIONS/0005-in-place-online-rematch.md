# ADR 0005 — In-place online rematch on the NetworkedMatch object

- **Status:** Accepted
- **Date:** 2026-07-08
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Supersedes:** none

## Context

Through M3.5, an online 2-player Cut-Throat round could be dealt, played to
completion, and its outcome shown — but there was no way to play another round
without both players leaving the room and re-matchmaking. M3.6 adds an
end-of-round overlay whose affirmative action, for online play, is a **rematch**:
deal a fresh round to the same two players in the same Photon room.

The `NetworkedMatch` `NetworkBehaviour` was designed as a *single-round* object:
it carries one seed, one move log, and a latched `DealReady` flag. Its
edge-detection in `Render()` assumed the move count only ever grows. A rematch
breaks all three assumptions at once — it needs a new seed, a truncated move
log, and it must not be mistaken for "the deal just landed" nor silently
swallow the new round's opening moves.

Two shapes were possible:

1. **Despawn + respawn** a new `NetworkedMatch` per round.
2. **Reuse** the existing `NetworkedMatch`, re-seeding it in place and carrying
   a round counter.

The rematch handshake itself also needed a policy: who can trigger a re-deal.

## Decision

**Reuse the `NetworkedMatch` in place** and extend its networked contract
(contract "v2"):

| Field / RPC | Purpose |
|---|---|
| `RoundNumber` (networked int) | 0 pre-deal, 1 on first deal, +1 per rematch. The current-round discriminator. |
| `Player1WantsRematch` / `Player2WantsRematch` (networked bool) | Standing rematch votes for the finished round. |
| `RPC_RequestRematch(byte playerIndex)` | Either client opts in; host records the vote. |
| `RoundStartedChanged` / `RematchVotesChanged` (C# events) | Local signals for the controller/UI. |
| `NextSeedProvider` (`Func<ulong>`, host-only, non-networked) | Supplies each rematch seed. Same injection pattern as the existing `MoveValidator`. |

`DealReady` keeps its original meaning — "both players have registered" — and
still latches once. It is **not** re-used as a per-round flag; `RoundNumber` is.
This deliberately leaves the proven M3.3 registration handshake untouched.

**Rematch requires both players to accept.** Each taps Rematch → `RPC_RequestRematch`
sets that player's vote. Only when *both* votes are true does the host, in a
single tick, publish a fresh seed, reset `MoveCount` to 0, clear both votes,
and increment `RoundNumber`. Because these mutate together, every client
observes them in one replicated snapshot and re-deals against consistent inputs.

**Edge detection is extracted to a pure, Unity-free `MatchSignalTracker`**
(`Pose.Core`). `NetworkedMatch.Render()` feeds it `(DealReady, RoundNumber,
MoveCount)` each frame and it reports deal / round-start / new-move-range
signals. The rematch move-count reset is the exact case a naive
`moveCount > _last` detector mishandles — it would sit at the previous round's
high-water mark and drop the new round's opening moves, desyncing the clients.
Pulling the sequencing into `Pose.Core` lets it be unit-tested without standing
up Fusion (`MatchSignalTrackerTests`).

## Rationale

1. **Reuse over respawn.** Re-seeding one object is a handful of networked
   field writes that Fusion replicates atomically. Despawn+respawn adds an
   object-lifecycle race — the client can observe the old object gone before
   the new one arrives — for no benefit, since nothing about the object other
   than its per-round inputs actually changes between rounds.
2. **Both-must-accept matches player expectation and is safe by default.** A
   single tap cannot restart the match under an opponent who is reading the
   final board or about to leave. It costs two extra networked bools and a
   "waiting for opponent…" button state, and mirrors how casual card/domino
   apps behave. (Alternatives — host-decides, either-player-starts — were
   rejected: both let one player's tap yank the other into a new round.)
3. **The seed stays client-side, as a provider seam.** The rematch seed is
   still host-clock-derived — the same trust gap the first-round seed already
   has (a malicious host can reroll its own hand). Rather than pretend to fix
   it here, `NextSeedProvider` is the single injection point M4's settlement
   pipeline replaces with a server-issued seed, for both first deal and
   rematch, without touching this contract.
4. **Pure edge-detection is the testable core of the risk.** The only genuinely
   subtle logic in this slice is "which moves are new across a round reset."
   Putting it in `Pose.Core` (the one assembly the EditMode tests reference)
   turns the desync risk into an assertion instead of an on-device surprise.

## Consequences

**Positive**

- Rematch adds no new networked object and no change to the M3.3 registration
  or M3.4 move-submission paths.
- The move-log capacity bound (`MaxMoves = 64`) stays per-round rather than
  cumulative, because a rematch truncates the log.
- The seed policy has exactly one seam (`NextSeedProvider`) for M4 to take over.

**Negative / accepted trade-offs**

- **Move log capacity is now reused across rounds.** A stale replicated read
  during the re-seed tick could momentarily expose old log entries; mitigated
  by the host mutating `Seed` / `MoveCount` / votes / `RoundNumber` in the same
  tick and by `MatchSignalTracker` refusing to replay indices below its cursor
  without a round advance.
- **Rematch is 2-player only.** Vote fields and `RPC_RequestRematch`'s
  `playerIndex` switch assume host=0 / joiner=1. The 3P/4P online slice will
  have to generalize these alongside the rest of `NetworkedMatch`.
- **A client-chosen rematch seed remains exploitable until M4.** Accepted for
  the same reasons as the first-round seed; tracked to the settlement milestone.

## Alternatives considered

- **Despawn + respawn a new `NetworkedMatch` per round.** Rejected: adds an
  object-lifecycle race for no benefit over in-place re-seeding.
- **Host-decides rematch** (host tap re-deals for both). Rejected: joiner is
  pulled into a new round without consent.
- **Either-player-starts** (first tap re-deals). Rejected: a mis-tap on the end
  screen instantly restarts the match for the opponent.
- **Keep edge detection inline in `NetworkedMatch.Render()`.** Rejected: the
  round-reset case is exactly what a naive monotonic check gets wrong, and it
  can't be unit-tested from the `Pose.Core`-only EditMode assembly.

## References

- `unity/Assets/_Project/Scripts/Net/NetworkedMatch.cs` — contract v2.
- `unity/Assets/_Project/Scripts/Net/OnlineMatchController.cs` — vote API, shared deal path, `NextSeedProvider`.
- `unity/Assets/_Project/Scripts/Core/Utils/MatchSignalTracker.cs` — extracted edge detection.
- `unity/Assets/Tests/EditMode/Utils/MatchSignalTrackerTests.cs` — sequencing tests, incl. the rematch desync guard.
- ADR [`0004-single-firebase-project.md`](0004-single-firebase-project.md) — single Photon AppID this room model runs on.
