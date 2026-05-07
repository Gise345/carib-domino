#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class JamaicanPartnerRulesTests
    {
        // Convention: Alice + Cara are team_a (partners across the table),
        // Bob + Dan are team_b. This is what Partnership.AlternatingPairs
        // produces when called with (alice, bob, cara, dan).
        private static readonly PlayerId Alice = new("alice");
        private static readonly PlayerId Bob = new("bob");
        private static readonly PlayerId Cara = new("cara");
        private static readonly PlayerId Dan = new("dan");

        // ---- Test helpers --------------------------------------------------

        /// <summary>
        /// Builds a 4-player MatchState with hand-picked tiles for each player —
        /// the standard partner shape (Alice+Cara vs Bob+Dan).
        /// </summary>
        private static MatchState MakeState(
            Tile[] aliceTiles,
            Tile[] bobTiles,
            Tile[] caraTiles,
            Tile[] danTiles,
            Chain? chain = null,
            int currentPlayerIndex = 0,
            int consecutivePassCount = 0,
            IReadOnlyList<Move>? history = null)
        {
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(aliceTiles),
                [Bob] = new Hand(bobTiles),
                [Cara] = new Hand(caraTiles),
                [Dan] = new Hand(danTiles),
            };
            return new MatchState(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: Partnership.AlternatingPairs(Alice, Bob, Cara, Dan),
                currentPlayerIndex: currentPlayerIndex,
                hands: hands,
                chain: chain ?? Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: consecutivePassCount,
                history: history ?? Array.Empty<Move>(),
                isOver: false);
        }

        // ---- Setup validation ----------------------------------------------

        [Test]
        public void Setup_Rejects_Match_With_Three_Players()
        {
            // Three players with three solo teams — wrong on the player count.
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(0, 0) }),
                [Bob] = new Hand(new[] { new Tile(1, 1) }),
                [Cara] = new Hand(new[] { new Tile(2, 2) }),
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara },
                partnership: Partnership.CutThroat(new[] { Alice, Bob, Cara }),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false);
            JamaicanPartnerRules rules = new();

            Assert.Throws<InvalidOperationException>(() => rules.GetLegalMoves(state));
        }

        [Test]
        public void Setup_Rejects_Cut_Throat_Partnership_For_Four_Players()
        {
            // Four players but a 4-solo-team Cut-Throat partnership — wrong shape.
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(0, 0) }),
                [Bob] = new Hand(new[] { new Tile(1, 1) }),
                [Cara] = new Hand(new[] { new Tile(2, 2) }),
                [Dan] = new Hand(new[] { new Tile(3, 3) }),
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: Partnership.CutThroat(new[] { Alice, Bob, Cara, Dan }),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false);
            JamaicanPartnerRules rules = new();

            Assert.Throws<InvalidOperationException>(() => rules.GetLegalMoves(state));
        }

        [Test]
        public void Setup_Rejects_Asymmetric_Partnership_Of_One_And_Three()
        {
            // Two teams, but split 1 + 3 — the wrong team-size shape.
            Team teamX = new(new TeamId("team_x"), new[] { Alice });
            Team teamY = new(new TeamId("team_y"), new[] { Bob, Cara, Dan });
            Partnership lopsided = new(new[] { teamX, teamY });

            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(0, 0) }),
                [Bob] = new Hand(new[] { new Tile(1, 1) }),
                [Cara] = new Hand(new[] { new Tile(2, 2) }),
                [Dan] = new Hand(new[] { new Tile(3, 3) }),
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: lopsided,
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false);
            JamaicanPartnerRules rules = new();

            Assert.Throws<InvalidOperationException>(() => rules.GetLegalMoves(state));
        }

        [Test]
        public void Setup_Accepts_AlternatingPairs_Partnership()
        {
            // Sanity: a well-formed 4-player AlternatingPairs setup must NOT throw.
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6) },
                bobTiles: new[] { new Tile(0, 1) },
                caraTiles: new[] { new Tile(2, 3) },
                danTiles: new[] { new Tile(4, 5) });
            JamaicanPartnerRules rules = new();

            Assert.DoesNotThrow(() => rules.GetLegalMoves(state));
        }

        [Test]
        public void Deal_Gives_Seven_Tiles_To_Each_Player()
        {
            // The Dealer hands out per the configured deal config; for partner play
            // this is the 4-player Cut-Throat config (7 each, full 28-tile set).
            // This test exists in the partner test file because it's the partner-
            // variant baseline a player would expect to see.
            Partnership partners = Partnership.AlternatingPairs(Alice, Bob, Cara, Dan);

            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                partners,
                new SeededRandomSource(0xCAFEBABEUL));

            Assert.That(state.Hands[Alice].Count, Is.EqualTo(7));
            Assert.That(state.Hands[Bob].Count, Is.EqualTo(7));
            Assert.That(state.Hands[Cara].Count, Is.EqualTo(7));
            Assert.That(state.Hands[Dan].Count, Is.EqualTo(7));
        }

        [Test]
        public void Holder_Of_DoubleSix_Leads_The_Round()
        {
            // [6|6] always lands in someone's hand for a 4-player double-six deal
            // (28 tiles, all dealt). That holder leads, per the canonical Jamaican
            // opening rule.
            Partnership partners = Partnership.AlternatingPairs(Alice, Bob, Cara, Dan);
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                partners,
                new SeededRandomSource(0xCAFEBABEUL));
            JamaicanPartnerRules rules = new();

            Hand starterHand = state.Hands[state.CurrentPlayer];
            Assert.That(starterHand.Contains(new Tile(6, 6)), Is.True);

            // Their only legal opening move is to pose [6|6].
            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);
            Assert.That(legal.Count, Is.EqualTo(1));
            PlaceMove opening = (PlaceMove)legal[0];
            Assert.That(opening.Tile, Is.EqualTo(new Tile(6, 6)));
        }

        // ---- Move enumeration delegation -----------------------------------

        [Test]
        public void Mid_Game_Player_Must_Match_Either_End()
        {
            // Chain is [3|5]. Only Alice's [3|0] (matches LEFT end's 3) is legal.
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(3, 0), new Tile(2, 2) },
                bobTiles: new[] { new Tile(0, 1) },
                caraTiles: new[] { new Tile(0, 4) },
                danTiles: new[] { new Tile(0, 6) },
                chain: chain,
                history: new Move[] { new PlaceMove(Dan, new Tile(3, 5), ChainEnd.Left) });
            JamaicanPartnerRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            PlaceMove place = (PlaceMove)legal[0];
            Assert.That(place.Tile, Is.EqualTo(new Tile(3, 0)));
            Assert.That(place.End, Is.EqualTo(ChainEnd.Left));
        }

        [Test]
        public void Pass_When_No_Tile_Matches()
        {
            // Chain ends are 3 / 5. Alice holds nothing matching either.
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(0, 1), new Tile(2, 2) },
                bobTiles: new[] { new Tile(0, 0) },
                caraTiles: new[] { new Tile(4, 4) },
                danTiles: new[] { new Tile(6, 6) },
                chain: chain,
                history: new Move[] { new PlaceMove(Dan, new Tile(3, 5), ChainEnd.Left) });
            JamaicanPartnerRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            Assert.That(legal[0], Is.InstanceOf<PassMove>());
        }

        [Test]
        public void Move_From_Wrong_Player_Is_Not_Legal()
        {
            // Defense in depth: even though IsLegal delegates to CutThroatRules,
            // re-asserting in the partner context guards against future drift.
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6) },
                bobTiles: new[] { new Tile(0, 1) },
                caraTiles: new[] { new Tile(2, 3) },
                danTiles: new[] { new Tile(4, 5) });
            JamaicanPartnerRules rules = new();

            // Alice is current; Bob attempting a move must be rejected.
            bool legal = rules.IsLegal(state, new PlaceMove(Bob, new Tile(0, 1), ChainEnd.Left));

            Assert.That(legal, Is.False);
        }

        [Test]
        public void Apply_Place_Removes_Tile_And_Rotates_Turn_To_Next_Player()
        {
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6), new Tile(0, 1) },
                bobTiles: new[] { new Tile(0, 0) },
                caraTiles: new[] { new Tile(2, 2) },
                danTiles: new[] { new Tile(3, 3) });
            JamaicanPartnerRules rules = new();

            MatchState after = rules.Apply(state, new PlaceMove(Alice, new Tile(6, 6), ChainEnd.Left));

            Assert.That(after.Hands[Alice].Contains(new Tile(6, 6)), Is.False);
            Assert.That(after.CurrentPlayer, Is.EqualTo(Bob));
        }

        [Test]
        public void Match_Ends_When_All_Four_Players_Pass_Consecutively()
        {
            // Both ends are 0 after the pose. Nobody holds a 0; every player must
            // pass. Four consecutive passes block the round.
            Chain chain = Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(1, 2) },
                bobTiles: new[] { new Tile(3, 4) },
                caraTiles: new[] { new Tile(5, 6) },
                danTiles: new[] { new Tile(1, 3) },
                chain: chain,
                history: new Move[] { new PlaceMove(Dan, new Tile(0, 0), ChainEnd.Left) });
            JamaicanPartnerRules rules = new();

            MatchState s1 = rules.Apply(state, new PassMove(Alice));
            MatchState s2 = rules.Apply(s1, new PassMove(Bob));
            MatchState s3 = rules.Apply(s2, new PassMove(Cara));
            MatchState s4 = rules.Apply(s3, new PassMove(Dan));

            Assert.That(s4.IsOver, Is.True);
            Assert.That(s4.ConsecutivePassCount, Is.EqualTo(4));
        }

        // ---- Domino end ----------------------------------------------------

        [Test]
        public void Domino_End_Sets_WinningTeamId_To_Dominoers_Team()
        {
            // Alice (team_a) plays her last tile [6|6]; Cara (her partner) plus
            // Bob and Dan still hold tiles. Winning team must be team_a.
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6) },
                bobTiles: new[] { new Tile(0, 1) },
                caraTiles: new[] { new Tile(2, 3) },
                danTiles: new[] { new Tile(4, 5) });
            JamaicanPartnerRules rules = new();

            MatchState after = rules.Apply(state, new PlaceMove(Alice, new Tile(6, 6), ChainEnd.Left));
            MatchOutcome? outcome = rules.GetOutcome(after);

            Assert.That(after.IsOver, Is.True);
            Assert.That(outcome!.Reason, Is.EqualTo(MatchEndReason.Domino));
            Assert.That(outcome.WinnerId, Is.EqualTo((PlayerId?)Alice));
            Assert.That(outcome.WinningTeamId, Is.EqualTo((TeamId?)after.Partnership.GetTeamOf(Alice)));
        }

        [Test]
        public void Domino_End_Score_Is_Sum_Of_Opposing_Teams_Pips_Only()
        {
            // Alice dominoes. team_a = {alice, cara}, team_b = {bob, dan}.
            // Score should be Bob.Pips + Dan.Pips — Cara's pips are NOT included.
            // Bob: [0|1]=1; Dan: [4|5]=9; Cara (excluded): [2|3]=5. Score = 1+9 = 10.
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6) },
                bobTiles: new[] { new Tile(0, 1) },
                caraTiles: new[] { new Tile(2, 3) },
                danTiles: new[] { new Tile(4, 5) });
            JamaicanPartnerRules rules = new();

            MatchState after = rules.Apply(state, new PlaceMove(Alice, new Tile(6, 6), ChainEnd.Left));
            MatchOutcome? outcome = rules.GetOutcome(after);

            Assert.That(outcome!.WinnerScore, Is.EqualTo(10));
        }

        [Test]
        public void Domino_End_Teammates_Pips_Are_Excluded_Even_If_Larger_Than_Opponents()
        {
            // The exclusion is structural, not score-dependent. Cara holds 31 pips
            // worth of tiles — far more than the opponents combined — but those
            // pips don't count for or against because Cara is on the winning team.
            // Bob: [0|1]=1; Dan: [0|2]=2; Cara: [5|5]+[5|6]+[4|6]=10+11+10=31.
            // Score must be Bob+Dan = 3, not Bob+Dan+Cara = 34.
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6) },
                bobTiles: new[] { new Tile(0, 1) },
                caraTiles: new[] { new Tile(5, 5), new Tile(5, 6), new Tile(4, 6) },
                danTiles: new[] { new Tile(0, 2) });
            JamaicanPartnerRules rules = new();

            // Alice plays [6|6] — she still has it, opening turn. Then Cara/Bob/Dan
            // can't follow because chain is 6/6. Eventually we'd block — but for
            // this scoring test we only need the domino end to fire. Fast path:
            // Alice plays her only tile [6|6], hand goes empty → Domino.
            MatchState after = rules.Apply(state, new PlaceMove(Alice, new Tile(6, 6), ChainEnd.Left));
            MatchOutcome? outcome = rules.GetOutcome(after);

            Assert.That(outcome!.Reason, Is.EqualTo(MatchEndReason.Domino));
            Assert.That(outcome.WinnerScore, Is.EqualTo(3));
        }

        // ---- Block end (corrected partner scoring) -------------------------

        [Test]
        public void Block_End_Single_Lowest_Pip_Player_Selects_Their_Team()
        {
            // The worked example from the M1 step 3 handoff:
            //   Alice (team_a)   5 pips
            //   Cara  (team_a)  30 pips  ← partner with very high pips
            //   Bob   (team_b)  10 pips
            //   Dan   (team_b)  12 pips
            // Lowest individual = 5 (Alice). team_a wins. Old/wrong rule said
            // combined A=35 vs B=22 → B wins; the corrected rule says A wins
            // because Alice's 5 is the single lowest.
            // We construct the block-end state directly so pips are exact.
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(2, 3) }),                                  // 5
                [Cara] = new Hand(new[] { new Tile(6, 6), new Tile(5, 6), new Tile(2, 5) }),   // 12+11+7 = 30
                [Bob] = new Hand(new[] { new Tile(4, 6) }),                                    // 10
                [Dan] = new Hand(new[] { new Tile(5, 4), new Tile(0, 3) }),                    // 9+3 = 12
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: Partnership.AlternatingPairs(Alice, Bob, Cara, Dan),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left),
                turnNumber: 4,
                consecutivePassCount: 4,
                history: Array.Empty<Move>(),
                isOver: true);
            JamaicanPartnerRules rules = new();

            MatchOutcome? outcome = rules.GetOutcome(state);

            Assert.That(outcome!.Reason, Is.EqualTo(MatchEndReason.Blocked));
            Assert.That(outcome.WinnerId, Is.EqualTo((PlayerId?)Alice));
            Assert.That(outcome.WinningTeamId, Is.EqualTo((TeamId?)state.Partnership.GetTeamOf(Alice)));
            // Score = opposing team_b's pips = Bob(10) + Dan(12) = 22.
            Assert.That(outcome.WinnerScore, Is.EqualTo(22));
        }

        [Test]
        public void Block_End_Tied_Lowest_Within_One_Team_Still_Wins_That_Team()
        {
            // Alice and Cara both hold 5 pips (tied for lowest); Bob has 10, Dan 12.
            // The tie is within team_a only — team_a still wins. WinnerId is the
            // first such player by Players-list order (Alice).
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(2, 3) }),    // 5
                [Cara] = new Hand(new[] { new Tile(1, 4) }),     // 5
                [Bob] = new Hand(new[] { new Tile(4, 6) }),      // 10
                [Dan] = new Hand(new[] { new Tile(5, 4), new Tile(0, 3) }), // 12
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: Partnership.AlternatingPairs(Alice, Bob, Cara, Dan),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left),
                turnNumber: 4,
                consecutivePassCount: 4,
                history: Array.Empty<Move>(),
                isOver: true);
            JamaicanPartnerRules rules = new();

            MatchOutcome? outcome = rules.GetOutcome(state);

            Assert.That(outcome!.IsDraw, Is.False);
            Assert.That(outcome.WinningTeamId, Is.EqualTo((TeamId?)state.Partnership.GetTeamOf(Alice)));
            // WinnerId is deterministic — the first player in Players-list order
            // who is on the winning team and at the min pip count. Alice precedes
            // Cara in the list, so Alice is the WinnerId.
            Assert.That(outcome.WinnerId, Is.EqualTo((PlayerId?)Alice));
            // Score = opposing team_b's pips = Bob(10) + Dan(12) = 22.
            Assert.That(outcome.WinnerScore, Is.EqualTo(22));
        }

        [Test]
        public void Block_End_Tied_Lowest_Across_Different_Teams_Is_A_Draw()
        {
            // Alice (team_a) and Bob (team_b) both at the minimum 5. The lowest
            // is split across teams → draw. WinnerId and WinningTeamId both null.
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(2, 3) }),    // 5
                [Bob] = new Hand(new[] { new Tile(1, 4) }),      // 5 (different tile, same pip total)
                [Cara] = new Hand(new[] { new Tile(6, 6) }),     // 12
                [Dan] = new Hand(new[] { new Tile(5, 6) }),      // 11
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: Partnership.AlternatingPairs(Alice, Bob, Cara, Dan),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left),
                turnNumber: 4,
                consecutivePassCount: 4,
                history: Array.Empty<Move>(),
                isOver: true);
            JamaicanPartnerRules rules = new();

            MatchOutcome? outcome = rules.GetOutcome(state);

            Assert.That(outcome!.IsDraw, Is.True);
            Assert.That(outcome.WinnerId, Is.Null);
            Assert.That(outcome.WinningTeamId, Is.Null);
            Assert.That(outcome.WinnerScore, Is.EqualTo(0));
        }

        [Test]
        public void Block_End_All_Four_Players_Tied_Is_A_Draw()
        {
            // Every player at exactly 5 pips. The lowest is found in both teams,
            // so the round is a draw — same outcome as any cross-team tie.
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(2, 3) }),    // 5
                [Bob] = new Hand(new[] { new Tile(0, 5) }),      // 5
                [Cara] = new Hand(new[] { new Tile(1, 4) }),     // 5
                [Dan] = new Hand(new[] { new Tile(5, 0) }),      // 5
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: Partnership.AlternatingPairs(Alice, Bob, Cara, Dan),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left),
                turnNumber: 4,
                consecutivePassCount: 4,
                history: Array.Empty<Move>(),
                isOver: true);
            JamaicanPartnerRules rules = new();

            MatchOutcome? outcome = rules.GetOutcome(state);

            Assert.That(outcome!.IsDraw, Is.True);
            Assert.That(outcome.WinnerId, Is.Null);
            Assert.That(outcome.WinningTeamId, Is.Null);
        }

        [Test]
        public void Block_End_Score_Excludes_Winning_Teams_Pips_Entirely()
        {
            // Re-asserts the structural rule: the winning team's combined pips —
            // including the winner's own — never appear in the score. Alice wins
            // with 5; team_a teammate Cara has 30 (excluded); opposing pips are
            // Bob(7) + Dan(8) = 15.
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(new[] { new Tile(2, 3) }),                                  // 5
                [Cara] = new Hand(new[] { new Tile(6, 6), new Tile(5, 6), new Tile(2, 5) }),   // 30
                [Bob] = new Hand(new[] { new Tile(3, 4) }),                                    // 7
                [Dan] = new Hand(new[] { new Tile(2, 6) }),                                    // 8
            };
            MatchState state = new(
                players: new[] { Alice, Bob, Cara, Dan },
                partnership: Partnership.AlternatingPairs(Alice, Bob, Cara, Dan),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left),
                turnNumber: 4,
                consecutivePassCount: 4,
                history: Array.Empty<Move>(),
                isOver: true);
            JamaicanPartnerRules rules = new();

            MatchOutcome? outcome = rules.GetOutcome(state);

            Assert.That(outcome!.WinnerScore, Is.EqualTo(15));
            // Sanity: Alice's 5 + Cara's 30 do NOT get added in.
            Assert.That(outcome.WinnerScore, Is.LessThan(20));
        }

        // ---- Replay determinism --------------------------------------------

        [Test]
        public void Replay_Of_Same_Seed_And_Move_Sequence_Produces_Identical_State()
        {
            // The foundational anti-cheat property for the M4 settlement validator:
            // a partner game replayed with identical seed, partnership, and moves
            // must produce bit-equivalent state at every step.
            Partnership partners = Partnership.AlternatingPairs(Alice, Bob, Cara, Dan);

            MatchState first = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                partners,
                new SeededRandomSource(0xCAFEBABEUL));
            MatchState second = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                partners,
                new SeededRandomSource(0xCAFEBABEUL));

            JamaicanPartnerRules rules = new();

            int safety = 200;
            while (!first.IsOver && safety-- > 0)
            {
                IReadOnlyList<Move> legalFirst = rules.GetLegalMoves(first);
                IReadOnlyList<Move> legalSecond = rules.GetLegalMoves(second);
                Assert.That(legalSecond.Count, Is.EqualTo(legalFirst.Count));

                Move chosen = legalFirst[0];
                first = rules.Apply(first, chosen);
                second = rules.Apply(second, chosen);

                AssertStatesEquivalent(first, second);
            }

            Assert.That(first.IsOver && second.IsOver, Is.True);
            MatchOutcome? oa = rules.GetOutcome(first);
            MatchOutcome? ob = rules.GetOutcome(second);
            Assert.That(oa!.Reason, Is.EqualTo(ob!.Reason));
            Assert.That(oa.WinnerId, Is.EqualTo(ob.WinnerId));
            Assert.That(oa.WinningTeamId, Is.EqualTo(ob.WinningTeamId));
            Assert.That(oa.WinnerScore, Is.EqualTo(ob.WinnerScore));
        }

        private static void AssertStatesEquivalent(MatchState a, MatchState b)
        {
            Assert.That(a.CurrentPlayerIndex, Is.EqualTo(b.CurrentPlayerIndex));
            Assert.That(a.TurnNumber, Is.EqualTo(b.TurnNumber));
            Assert.That(a.ConsecutivePassCount, Is.EqualTo(b.ConsecutivePassCount));
            Assert.That(a.IsOver, Is.EqualTo(b.IsOver));
            Assert.That(a.Chain.Count, Is.EqualTo(b.Chain.Count));
            for (int i = 0; i < a.Players.Count; i++)
            {
                PlayerId p = a.Players[i];
                CollectionAssert.AreEqual(a.Hands[p], b.Hands[p]);
            }
        }
    }
}
