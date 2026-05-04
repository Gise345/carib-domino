#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class BlockRulesTests
    {
        private static readonly PlayerId Alice = new("alice");
        private static readonly PlayerId Bob = new("bob");
        private static readonly PlayerId Cara = new("cara");
        private static readonly PlayerId Dan = new("dan");

        // ---- Test helpers --------------------------------------------------

        /// <summary>
        /// Builds a contrived two-player MatchState with hand-picked tiles, useful for
        /// pinpoint legality tests where a real seeded deal would not give us the
        /// specific configuration we want to assert against.
        /// </summary>
        private static MatchState MakeState(
            Tile[] aliceTiles,
            Tile[] bobTiles,
            Chain? chain = null,
            int currentPlayerIndex = 0,
            int consecutivePassCount = 0,
            IReadOnlyList<Move>? history = null)
        {
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = new Hand(aliceTiles),
                [Bob] = new Hand(bobTiles),
            };
            return new MatchState(
                players: new[] { Alice, Bob },
                currentPlayerIndex: currentPlayerIndex,
                hands: hands,
                chain: chain ?? Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: consecutivePassCount,
                history: history ?? Array.Empty<Move>(),
                isOver: false);
        }

        // ---- Opening turn --------------------------------------------------

        [Test]
        public void Opening_Turn_Returns_Single_Move_For_Leading_Tile()
        {
            // Alice has [6|6] (highest double); Bob has lower tiles. Per opening rule,
            // only Alice's [6|6] is legal on turn 1.
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6), new Tile(0, 1) },
                bobTiles: new[] { new Tile(2, 2), new Tile(3, 5) });
            BlockRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            Assert.That(legal[0], Is.InstanceOf<PlaceMove>());
            PlaceMove place = (PlaceMove)legal[0];
            Assert.That(place.Tile, Is.EqualTo(new Tile(6, 6)));
            Assert.That(place.Player, Is.EqualTo(Alice));
        }

        [Test]
        public void Opening_Falls_Back_To_Highest_Single_When_No_Doubles_In_Play()
        {
            // Carefully crafted: no doubles in either hand. Alice's highest pip tile
            // is [5|6] (11 pips). Bob's highest is [4|6] (10 pips). Alice should lead
            // with [5|6].
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(5, 6), new Tile(0, 1) },
                bobTiles: new[] { new Tile(4, 6), new Tile(0, 2) });
            BlockRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            PlaceMove place = (PlaceMove)legal[0];
            Assert.That(place.Tile, Is.EqualTo(new Tile(5, 6)));
        }

        // ---- Legal-move enumeration ---------------------------------------

        [Test]
        public void Tile_Matching_Only_Left_End_Has_Only_Left_As_Legal()
        {
            // Chain: [3|5]. Player tile [3|0] matches LEFT only.
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(3, 0), new Tile(2, 2) }, // [2|2] won't match either end
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 5), ChainEnd.Left) });
            BlockRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            PlaceMove place = (PlaceMove)legal[0];
            Assert.That(place.End, Is.EqualTo(ChainEnd.Left));
        }

        [Test]
        public void Tile_Matching_Only_Right_End_Has_Only_Right_As_Legal()
        {
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(5, 1), new Tile(2, 2) },
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 5), ChainEnd.Left) });
            BlockRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            PlaceMove place = (PlaceMove)legal[0];
            Assert.That(place.End, Is.EqualTo(ChainEnd.Right));
        }

        [Test]
        public void Tile_Matching_Both_Ends_Has_Both_As_Legal_Placements()
        {
            // Chain seeded with double [3|3] so both ends equal 3 (a state genuinely
            // reachable in real play). Alice has [3|5] — matches both ends.
            Chain chain = Chain.Empty.Place(new Tile(3, 3), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(3, 5), new Tile(0, 1) },
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 3), ChainEnd.Left) });
            BlockRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            int placementsFor35 = 0;
            foreach (Move m in legal)
            {
                if (m is PlaceMove p && p.Tile == new Tile(3, 5))
                {
                    placementsFor35++;
                }
            }
            Assert.That(placementsFor35, Is.EqualTo(2));
        }

        [Test]
        public void Tile_Matching_Neither_End_Is_Not_Legal()
        {
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(0, 1) }, // matches neither 3 nor 5
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 5), ChainEnd.Left) });
            BlockRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            Assert.That(legal[0], Is.InstanceOf<PassMove>());
        }

        [Test]
        public void Player_With_No_Playable_Tile_Has_Only_Pass_Move()
        {
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(0, 1), new Tile(2, 2), new Tile(4, 4) },
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 5), ChainEnd.Left) });
            BlockRules rules = new();

            IReadOnlyList<Move> legal = rules.GetLegalMoves(state);

            Assert.That(legal.Count, Is.EqualTo(1));
            Assert.That(legal[0], Is.InstanceOf<PassMove>());
        }

        [Test]
        public void Player_With_Playable_Tile_Cannot_Pass()
        {
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(3, 1) }, // matches LEFT
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 5), ChainEnd.Left) });
            BlockRules rules = new();

            bool legal = rules.IsLegal(state, new PassMove(Alice));

            Assert.That(legal, Is.False);
        }

        [Test]
        public void Move_From_Wrong_Player_Is_Not_Legal()
        {
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6) },
                bobTiles: new[] { new Tile(0, 1) });
            BlockRules rules = new();

            // Alice is current; submitting a move as Bob must be rejected.
            bool legal = rules.IsLegal(state, new PlaceMove(Bob, new Tile(6, 6), ChainEnd.Left));

            Assert.That(legal, Is.False);
        }

        // ---- Apply ---------------------------------------------------------

        [Test]
        public void Apply_Place_Removes_Tile_From_Hand_And_Updates_Chain()
        {
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6), new Tile(0, 1) },
                bobTiles: new[] { new Tile(0, 0) });
            BlockRules rules = new();

            MatchState after = rules.Apply(state, new PlaceMove(Alice, new Tile(6, 6), ChainEnd.Left));

            Assert.That(after.Hands[Alice].Contains(new Tile(6, 6)), Is.False);
            Assert.That(after.Hands[Alice].Count, Is.EqualTo(1));
            Assert.That(after.Chain.Count, Is.EqualTo(1));
            Assert.That(after.Chain.LeftEnd, Is.EqualTo((byte)6));
            Assert.That(after.Chain.RightEnd, Is.EqualTo((byte)6));
        }

        [Test]
        public void Apply_Place_Rotates_Turn_To_Next_Player()
        {
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6), new Tile(0, 1) },
                bobTiles: new[] { new Tile(0, 0) });
            BlockRules rules = new();

            MatchState after = rules.Apply(state, new PlaceMove(Alice, new Tile(6, 6), ChainEnd.Left));

            Assert.That(after.CurrentPlayer, Is.EqualTo(Bob));
            Assert.That(after.TurnNumber, Is.EqualTo(1));
            Assert.That(after.ConsecutivePassCount, Is.EqualTo(0));
        }

        [Test]
        public void Apply_Pass_Rotates_Turn_And_Increments_Pass_Count()
        {
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(0, 1), new Tile(2, 2) },
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 5), ChainEnd.Left) });
            BlockRules rules = new();

            MatchState after = rules.Apply(state, new PassMove(Alice));

            Assert.That(after.CurrentPlayer, Is.EqualTo(Bob));
            Assert.That(after.ConsecutivePassCount, Is.EqualTo(1));
            Assert.That(after.TurnNumber, Is.EqualTo(1));
            Assert.That(after.Chain.Count, Is.EqualTo(1)); // chain unchanged
        }

        [Test]
        public void Apply_Throws_When_Move_Is_Illegal()
        {
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6), new Tile(0, 1) },
                bobTiles: new[] { new Tile(0, 0) });
            BlockRules rules = new();

            // Tile [0|1] not allowed on opening turn (only the leading double is).
            Assert.Throws<InvalidOperationException>(() =>
                rules.Apply(state, new PlaceMove(Alice, new Tile(0, 1), ChainEnd.Left)));
        }

        [Test]
        public void Apply_Place_Tile_Not_In_Hand_Is_Rejected()
        {
            Chain chain = Chain.Empty.Place(new Tile(3, 5), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(3, 0) },
                bobTiles: new[] { new Tile(0, 0) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(3, 5), ChainEnd.Left) });
            BlockRules rules = new();

            // Alice doesn't hold [5|2]; cannot play it even though it would otherwise match RIGHT.
            Assert.Throws<InvalidOperationException>(() =>
                rules.Apply(state, new PlaceMove(Alice, new Tile(5, 2), ChainEnd.Right)));
        }

        [Test]
        public void Apply_To_Finished_Match_Throws()
        {
            // Construct an already-finished state: Alice has empty hand.
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = Hand.Empty,
                [Bob] = new Hand(new[] { new Tile(0, 1) }),
            };
            MatchState state = new(
                players: new[] { Alice, Bob },
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty.Place(new Tile(6, 6), ChainEnd.Left),
                turnNumber: 1,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: true);
            BlockRules rules = new();

            Assert.Throws<InvalidOperationException>(() =>
                rules.Apply(state, new PassMove(Alice)));
        }

        // ---- Match end: Domino --------------------------------------------

        [Test]
        public void Domino_End_Marks_Match_Over_And_Records_Winner()
        {
            // Set up: Alice has only [6|6] left. She plays it; her hand goes to 0.
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(6, 6) },
                bobTiles: new[] { new Tile(0, 1), new Tile(2, 3) });
            BlockRules rules = new();

            MatchState after = rules.Apply(state, new PlaceMove(Alice, new Tile(6, 6), ChainEnd.Left));
            MatchOutcome? outcome = rules.GetOutcome(after);

            Assert.That(after.IsOver, Is.True);
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome!.Reason, Is.EqualTo(MatchEndReason.Domino));
            Assert.That(outcome.WinnerId, Is.EqualTo((PlayerId?)Alice));
            // Bob's remaining pips: [0|1]=1 + [2|3]=5 = 6.
            Assert.That(outcome.WinnerScore, Is.EqualTo(6));
        }

        // ---- Match end: Block ---------------------------------------------

        [Test]
        public void Block_End_When_All_Players_Pass_Consecutively()
        {
            // Construct a chain with both ends = 0; both players hold no zeros.
            Chain chain = Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(1, 2), new Tile(3, 4) }, // no zeros
                bobTiles: new[] { new Tile(5, 6), new Tile(1, 3) },   // no zeros, no overlap with Alice
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(0, 0), ChainEnd.Left) });
            BlockRules rules = new();

            MatchState afterAlicePass = rules.Apply(state, new PassMove(Alice));
            MatchState afterBobPass = rules.Apply(afterAlicePass, new PassMove(Bob));

            Assert.That(afterBobPass.IsOver, Is.True);
            Assert.That(afterBobPass.ConsecutivePassCount, Is.EqualTo(2));
            MatchOutcome? outcome = rules.GetOutcome(afterBobPass);
            Assert.That(outcome!.Reason, Is.EqualTo(MatchEndReason.Blocked));
        }

        [Test]
        public void Block_End_Lowest_Pip_Total_Wins()
        {
            // Alice 1+2 + 3+4 = 10 pips; Bob 5+6 + 1+3 = 15 pips. Alice wins.
            // All tiles unique across both hands, as in a real deal.
            Chain chain = Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(1, 2), new Tile(3, 4) },
                bobTiles: new[] { new Tile(5, 6), new Tile(1, 3) },
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(0, 0), ChainEnd.Left) });
            BlockRules rules = new();

            MatchState s1 = rules.Apply(state, new PassMove(Alice));
            MatchState s2 = rules.Apply(s1, new PassMove(Bob));
            MatchOutcome? outcome = rules.GetOutcome(s2);

            Assert.That(outcome!.WinnerId, Is.EqualTo((PlayerId?)Alice));
            Assert.That(outcome.WinnerScore, Is.EqualTo(15)); // Bob's pips
        }

        [Test]
        public void Block_End_Tie_For_Lowest_Is_A_Draw()
        {
            // Both hands sum to 10.
            Chain chain = Chain.Empty.Place(new Tile(0, 0), ChainEnd.Left);
            MatchState state = MakeState(
                aliceTiles: new[] { new Tile(1, 2), new Tile(3, 4) }, // 10
                bobTiles: new[] { new Tile(2, 4), new Tile(1, 3) },   // 10
                chain: chain,
                history: new Move[] { new PlaceMove(Bob, new Tile(0, 0), ChainEnd.Left) });
            BlockRules rules = new();

            MatchState s1 = rules.Apply(state, new PassMove(Alice));
            MatchState s2 = rules.Apply(s1, new PassMove(Bob));
            MatchOutcome? outcome = rules.GetOutcome(s2);

            Assert.That(outcome!.IsDraw, Is.True);
            Assert.That(outcome.WinnerId, Is.Null);
            Assert.That(outcome.WinnerScore, Is.EqualTo(0));
        }

        // ---- Full-game flow -----------------------------------------------

        [Test]
        public void Two_Player_Random_Game_Plays_To_A_Valid_End_State()
        {
            // Use a real seeded deal and play to completion by always picking the
            // first legal move. The game must terminate (Domino or Blocked) within
            // a reasonable upper bound — there are only 28 tiles.
            MatchState state = Dealer.Deal(
                DealConfig.BlockDoubleSix,
                new[] { Alice, Bob },
                new SeededRandomSource(0xC0FFEEUL));
            BlockRules rules = new();

            int safetyLimit = 200;
            int turns = 0;
            while (!state.IsOver && turns++ < safetyLimit)
            {
                IReadOnlyList<Move> legal = rules.GetLegalMoves(state);
                Assert.That(legal.Count, Is.GreaterThan(0), "Mid-match must always offer at least one legal move.");
                state = rules.Apply(state, legal[0]);
            }

            Assert.That(state.IsOver, Is.True, $"Match did not terminate within {safetyLimit} turns.");
            MatchOutcome? outcome = rules.GetOutcome(state);
            Assert.That(outcome, Is.Not.Null);
            Assert.That(
                outcome!.Reason,
                Is.AnyOf(MatchEndReason.Domino, MatchEndReason.Blocked));
        }

        [Test]
        public void Four_Player_Random_Game_Plays_To_A_Valid_End_State()
        {
            MatchState state = Dealer.Deal(
                DealConfig.BlockDoubleSix,
                new[] { Alice, Bob, Cara, Dan },
                new SeededRandomSource(0xC0FFEEUL));
            BlockRules rules = new();

            int safetyLimit = 200;
            while (!state.IsOver && safetyLimit-- > 0)
            {
                IReadOnlyList<Move> legal = rules.GetLegalMoves(state);
                state = rules.Apply(state, legal[0]);
            }

            Assert.That(state.IsOver, Is.True);
        }

        // ---- Replay determinism (foundation for M4 settlement validator) ---

        [Test]
        public void Replay_Of_Same_Move_Sequence_Produces_Identical_State_At_Every_Step()
        {
            // The foundational anti-cheat property: given the same seed and the same
            // ordered move list, every intermediate MatchState must be identical
            // across runs. The eventual TS validator on the server (M4) will rely on
            // this exactly.
            MatchState first = Dealer.Deal(
                DealConfig.BlockDoubleSix,
                new[] { Alice, Bob, Cara, Dan },
                new SeededRandomSource(0xCAFEBABEUL));
            MatchState second = Dealer.Deal(
                DealConfig.BlockDoubleSix,
                new[] { Alice, Bob, Cara, Dan },
                new SeededRandomSource(0xCAFEBABEUL));

            BlockRules rules = new();

            // Drive both states by always picking the first legal move; they must
            // remain bit-equivalent at every step.
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
            AssertOutcomesEquivalent(rules.GetOutcome(first), rules.GetOutcome(second));
        }

        // ---- Equivalence helpers ------------------------------------------

        private static void AssertStatesEquivalent(MatchState a, MatchState b)
        {
            Assert.That(a.CurrentPlayerIndex, Is.EqualTo(b.CurrentPlayerIndex));
            Assert.That(a.TurnNumber, Is.EqualTo(b.TurnNumber));
            Assert.That(a.ConsecutivePassCount, Is.EqualTo(b.ConsecutivePassCount));
            Assert.That(a.IsOver, Is.EqualTo(b.IsOver));
            Assert.That(a.Chain.Count, Is.EqualTo(b.Chain.Count));
            for (int i = 0; i < a.Chain.Count; i++)
            {
                Assert.That(a.Chain.Tiles[i].Tile, Is.EqualTo(b.Chain.Tiles[i].Tile));
                Assert.That(a.Chain.Tiles[i].LeftPip, Is.EqualTo(b.Chain.Tiles[i].LeftPip));
                Assert.That(a.Chain.Tiles[i].RightPip, Is.EqualTo(b.Chain.Tiles[i].RightPip));
            }
            for (int i = 0; i < a.Players.Count; i++)
            {
                PlayerId p = a.Players[i];
                CollectionAssert.AreEqual(a.Hands[p], b.Hands[p]);
            }
        }

        private static void AssertOutcomesEquivalent(MatchOutcome? a, MatchOutcome? b)
        {
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(a!.Reason, Is.EqualTo(b!.Reason));
            Assert.That(a.WinnerId, Is.EqualTo(b.WinnerId));
            Assert.That(a.WinnerScore, Is.EqualTo(b.WinnerScore));
        }
    }
}
