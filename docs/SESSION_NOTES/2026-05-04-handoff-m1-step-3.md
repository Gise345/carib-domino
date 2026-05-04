# Session handoff — M1 step 3 (Jamaican Partner ruleset)

**Date written:** 2026-05-04
**Author of handoff:** Claude Opus 4.7 (1M context), end of previous session
**For:** New Claude session starting M1 step 3 implementation

---

## How to use this document

1. Paste the contents of the **"Kickoff prompt"** section at the bottom into a fresh Claude Code session.
2. Claude will read [CLAUDE.md](../../CLAUDE.md) (auto-loaded) and the references this document points to.
3. It should re-confirm the plan with you before writing code (per the proposal-first habit) — answer briefly, then it'll proceed.

---

## Where the project stands

- **Branch:** `main`, ahead of `origin/main` by 4 commits at end of previous session.
- **Last 4 commits** (most recent first):
  - `817609d chore(unity): meta files for Pose.Core + Pose.Core.Tests`
  - `2ed0abd fix(test): replace Is.AnyOf with Or.EqualTo`
  - `897f356 feat(core): add Block dominoes rule engine + comprehensive tests (M1 step 2)`
  - `6f8c688 chore(unity): scaffold _Project subtree on top of Unity Editor 6000.4.5f1`
- **All 48 EditMode tests pass** in the Unity Test Runner.
- **Unity Editor:** 6000.4.5f1 (URP 3D template), pinned in `unity/ProjectSettings/ProjectVersion.txt`.

## What M1 step 3 is

Implement the **Jamaican Partner** ruleset in pure C# under `Pose.Core`, alongside renaming the existing `BlockRules` (built in M1 step 2) to `CutThroatRules` and fixing three correctness bugs in its deal counts. End state: two playable single-round variants (Cut-Throat and Partner), full unit-test coverage, replay-determinism preserved across both.

## Plan: three phases, three commits, one session

### Phase 1 — Refactor: `Block` → `CutThroat` + correct deal counts

Pure-rename + bug-fix. The variant that exists today as `BlockRules` is what Jamaicans call **Cut-Throat**; per Giselle's instruction, that name is used throughout the app.

Three correctness bugs in the current implementation, all in deal counts (the 4-player path is correct):

| Players | Current (wrong) | Jamaican rule |
|---|---|---|
| 2 | 7 each, 14 sleeping | **14 each, no sleeping tiles** |
| 3 | 7 each, 7 sleeping | **9 each, [0\|0] removed entirely** |
| 4 | 7 each ✓ | 7 each ✓ |

Files renamed:
- `unity/Assets/_Project/Scripts/Core/Rules/BlockRules.cs` → `CutThroatRules.cs`
- `unity/Assets/Tests/EditMode/Rules/BlockRulesTests.cs` → `CutThroatRulesTests.cs`
- `BlockRules` type → `CutThroatRules`
- `BlockRulesTests` type → `CutThroatRulesTests`

API change in `DealConfig`:
```csharp
// Before
DealConfig.BlockDoubleSix    // single property, fixed 7-per-hand

// After
DealConfig.CutThroatDoubleSix(int playerCount)
//   2P → 14 each, full 28-tile set
//   3P → 9 each, [0|0] removed (27 tiles)
//   4P → 7 each, full 28-tile set
//   other → throws ArgumentException
```

XML doc on `MatchState.Players`: clarify that the list is in **turn order**, and note that for Jamaican variants this corresponds to **anticlockwise seating** at a physical table. The engine itself is direction-agnostic — it just iterates the list.

New tests:
- 3-player Cut-Throat deals 9 to each; nobody holds [0|0]
- 2-player Cut-Throat deals 14 to each; nothing sleeps
- Rejects 1-player and 5+-player counts

### Phase 2 — Partnership types (no new variant yet)

Adds the Team/Partnership concept and threads it through state and outcomes, but `CutThroatRules` keeps working unchanged (cut-throat = each player on a solo team).

New files:
- `Core/State/TeamId.cs` — strong-typed identifier, mirrors `PlayerId`
- `Core/State/Team.cs` — `{ TeamId Id; IReadOnlyList<PlayerId> Members; }`
- `Core/State/Partnership.cs` — `{ IReadOnlyList<Team> Teams; GetTeamOf(player); }`
  - factory: `Partnership.CutThroat(IEnumerable<PlayerId>)` — each player solo
  - factory: `Partnership.AlternatingPairs(p1, p2, p3, p4)` — positions 0+2 vs 1+3
- `Tests/EditMode/State/TeamIdTests.cs`
- `Tests/EditMode/State/PartnershipTests.cs`

Modified files:
- `Core/State/MatchState.cs` — new `Partnership Partnership { get; }` field, threaded through ctor and `With(...)`.
- `Core/State/MatchOutcome.cs` — new `TeamId? WinningTeamId { get; }`. Set on every non-draw win.
- `Core/Rules/Dealer.cs` — accepts `Partnership` parameter.
- `Core/Rules/CutThroatRules.cs` — `GetOutcome` populates `WinningTeamId` from the cut-throat partnership.
- `Tests/EditMode/Rules/CutThroatRulesTests.cs` — ~5 small assertions added for `WinningTeamId`.
- `Tests/EditMode/State/MatchStateTests.cs` — coverage for new field.
- `Tests/EditMode/Rules/DealerTests.cs` — pass `Partnership` in tests.

ADR: `docs/DECISIONS/0003-team-and-partnership-model.md` — documents why teams are first-class on `MatchState` (vs. owned privately by the rule engine), the `WinnerId` (player) vs. `WinningTeamId` (team) split semantics, and the cut-throat-as-solo-teams unification.

### Phase 3 — `JamaicanPartnerRules`

The new variant.

New files:
- `Core/Rules/JamaicanPartnerRules.cs`
- `Tests/EditMode/Rules/JamaicanPartnerRulesTests.cs`

Behaviour:
- Requires exactly 4 players
- Requires an `AlternatingPairs` partnership (rejects malformed partnerships)
- Deals 7 each, full 28-tile set (no sleeping)
- Round 1: holder of [6|6] poses [6|6] (handled by existing `StartingPlayerRule` — for 4-player double-six, [6|6] is always dealt)
- Mid-game move enumeration: same as Cut-Throat (must match LEFT or RIGHT end)
- **Domino end:** dominoing player's *team* wins. Team score = sum of *opposing team's* pip totals. Teammate's pips don't count for or against.
- **Block end:** find the single player with the lowest pip count. That player's team wins. Score = sum of opposing team's pip totals.
  - If the tied lowest is on ONE team only → that team wins.
  - If the tied lowest is across DIFFERENT teams → draw (`WinningTeamId` null).
  - If all four tied → draw.

## Confirmed decisions (from previous session)

1. **Three-commit, one-session plan.** Confirmed.
2. **Phase 1 deal-count fix bundled with the rename.** Confirmed.
3. **Keep "Block / Draw (Anglo)" in [docs/ARCHITECTURE.md §8.1](../ARCHITECTURE.md) as a separate future variant.** Confirmed — it has different scoring conventions and different audience (US/UK), so it isn't a duplicate of Cut-Throat.
4. **Corrected Partner Block-end scoring:** *individual* lowest pip count selects the winning team, per the source rules ("the player who has the lowest card, his team wins"). Confirmed. (The previous-session proposal originally said "lowest *combined* team pips wins" — that was wrong. Worked example: Alice (team A) 5, Cara (team A, partner) 30, Bob (team B) 10, Dan (team B) 12. Old/wrong: A combined = 35 vs. B combined = 22 → team B wins. Corrected: lowest is Alice's 5 → team A wins. The corrected rule is what the source text says and what we'll implement.)
5. **Write ADR 0003** documenting the partnership model. Confirmed.

## Test coverage targets

`CutThroatRulesTests` (existing, lightly extended) — ~3 new tests for the corrected deal counts plus ~5 added `WinningTeamId` assertions.

`PartnershipTests` (new) — ~10 tests covering TeamId equality, team membership, factories (`CutThroat`, `AlternatingPairs`), validation (rejects partner-of-self, rejects wrong player count for AlternatingPairs), `GetTeamOf` correctness.

`JamaicanPartnerRulesTests` (new) — ~22 tests:
- Setup: rejects ≠4 players, rejects malformed partnership, deals 7 each, [6|6] holder leads
- Domino end: winning team set; team score = both opponents' pips; teammate's pips excluded
- Block end (corrected scoring): lowest individual pips selects winning team; tied within team → still wins; tied across teams → draw; all four tied → draw
- Wrong-team move rejection (defence-in-depth — covered by inherited move-validation but worth re-verifying with partnership context)
- Replay determinism: same seed + same partnership + same move sequence → bit-identical state at every step (the foundation for the M4 server-side validator)

## Workflow note (Unity meta-file dance)

After each phase's `.cs` files are written, the user opens Unity, lets it auto-generate `.meta` files for the new files, runs Test Runner → EditMode → Run All, and confirms green. Then both the source and the generated `.meta` files are committed together. (For the rename in Phase 1, the renamed `.cs.meta` files travel with their `.cs` automatically because Unity tracks via GUID-in-meta, not by filename.)

## Reference content for the app's Help section (NOT this milestone)

The user has provided the following Domino Game Playing Tips text that should appear in the Help/Tutorial UI of the eventual app. **It is NOT part of M1 step 3** — it's reference material for whichever future milestone builds the Help screen (likely after M1 step 4 Unity scene work, possibly bundled with M7 monetization since the help screen needs UI infrastructure). Preserve it verbatim; do not paraphrase.

```
Domino Game Playing Tips
Our aim to share the long held secrets of the domino pros; the tips to move from beginner to good and from good to great!

Cut Throat
In the Cut Throat game each player plays for themselves, so wherever a reference is made to an opponent it will be all the board.

Pose your strongest card, and not necessarily the biggest double. For example if you have the cards 1-2, 2-2, 6-6, 0-2, 4-3, 5-3 and 5-2; double dose (2-2) is your best pose as 2 is your strongest card and posing this will have the game start with 2 at both ends. It is very hard for you not to get a chance to play your double if it is the only card with that number you have, in this case double 6 (6-6).
Remember which suit/suits your opponents have passed on in the current round as this will be vital towards the end when you are trying to make your winning play (game-reading)
Never sort your cards as it gives your opponent an idea of where your strong cards and doubles are by just watching where you play from (game-reading)
Killing your opponent's double is a good way to keep them out of the game barring a block and a higher count than you (INSURANCE).
Watch what your partner/opponent poses because that card's suit is probably the strongest he/she has.
Before blocking evaluate your chances of having the lowest count: look on the board and take note of the cards not played as yet and look at your opponents/partner's hands and evaluate if they could count lower or higher than you.
If you have a very strong hand meaning that you have at least five of the same suit refrain cutting the suit; only play the suit if it isn't at either end. This would be useful as this allows the other cards of the suit to be played out making you the only one with the rest of that suit.
Always play a card that you have more than one of the suits of the card unless forced. Never play a card that you have no more of the suits ( this will allow for a better possibility of being passed)
When in doubt, play your highest value card first as this will go against you negatively in a block.
Blocking the game is usually your only hope of winning after being pass for 3 or more times. You may have to sacrifice a double to achieve this.

Partner
The tips for Cut Throat Applies to Partner Games, just remember that your opponents will be the other team and not your partner.

Rule of thumb: Don't play on the exposed end of the card your partner plays unless you have no option. This will also tell your partner and other players what you are weak on (Reading Partner)
Try not to play cards that will aim your opponent in passing your partner. For example if your partner passed on threes (3) it would be best for you to play a card that will prevent three from coming on to the board
Being consistent with how you play is essential in your game play as that is the only way your partner can understand how you play i.e. if your hand is strong with threes then ensure your partner will pick up on that; don't play cards that you don't have (teamwork)
Ensure you play your strongest suit so as to let your partner know what to play to help you win.
If the opponent has the pose, you and your partner must aim to pass the opponents. For example: The Player Underneath Your Hand has the pose, it would be very good if you passed him giving your partner the pose. (Useful in ensuring victory)
Always think twice before killing a double. The feeling may be nice but your partner may suffer. Only kill your partners double if he is unable to win, so it will all be up to you to win.
```

## Source: Jamaican Cut-Throat & Partner rules

These are the canonical rules per Giselle (2026-05-04). They override anything in [docs/ARCHITECTURE.md §8.1](../ARCHITECTURE.md) on these specific variants.

```
Materials – 28 dominoes; 7 of each suit 0-0…6-6.
Players: 2-4. 2P=14 tiles each. 3P=9 each, [0|0] omitted. 4P=7 each.
Play is anticlockwise.

Three Jamaican variants exist: Cut-Throat, Partner, French. Pose only does Cut-Throat and Partner.

Cut-Throat
- Round 1: holder of [6|6] poses [6|6] (mandatory, even if not their strongest suit).
- Subsequent rounds: previous round's winner poses any tile of their choice.
- Pass if you can't match either end. No drawing — boneyard not used.
- Domino end: someone empties their hand → 1 point that round.
- Block end: lowest pip count wins → 1 point.
- Block tie: draw, replay with [6|6] posed, winner of replay gets 2 points.

Partner
- Same as Cut-Throat, exactly 4 players, partners across the table.
- Either teammate may pose for the team.
- Block end: player with lowest pip count → their team wins.
- Block tie within one team: that team wins. Across teams: draw, replay rule.

Six-Love variant (multi-round, OUT OF SCOPE for the round engine):
- First to 6 wins, but opponents must have 0 at the moment of victory.
- "Game bruk" = if all reach ≥1, game resets.
- "One All Play Two": when all at 1, next round poses [6|6], winner gets 2.
```

---

## Kickoff prompt for the new session

Paste the block below into a fresh Claude Code session in this repo. Everything Claude needs to know is either in the prompt itself or one read away.

````
Continuing the Pose: Caribbean Dominoes project. We're starting M1 step 3
(Jamaican Partner ruleset). Before doing anything else, read in this order:

1. CLAUDE.md (auto-loaded — operating instructions)
2. docs/SESSION_NOTES/2026-05-04-handoff-m1-step-3.md (the full plan,
   confirmed decisions, source rules, and reference content)
3. docs/ARCHITECTURE.md §7.2 and §8.1 (rule engine parity, variant table)
4. git log --oneline -8 (current state)

Once read, summarise back to me in 4-6 bullets:
- The three-phase plan (rename, partnerships, JamaicanPartnerRules)
- Why "Block" is being renamed to "Cut-Throat"
- The three deal-count bugs being fixed in Phase 1
- The corrected Partner Block-end scoring (individual lowest, not combined)
- What's explicitly out of scope (multi-round mechanics, six-love, etc.)

Then re-confirm or push back on the plan. Once aligned, begin Phase 1 with
small, reviewable commits per the plan in the handoff document.

Open Unity at the end of each phase so it generates .meta files for any new
.cs files; commit source + meta together per phase.
````
