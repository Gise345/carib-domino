#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// Convergence guarantees that M3.4's networked move log relies on. The
    /// network layer replicates ONLY the move sequence (plus the deal seed
    /// + player order from M3.3) — both clients then run the deterministic
    /// rule engine locally and must arrive at identical MatchState. These
    /// tests assert the underlying engine actually delivers that guarantee
    /// without involving Fusion: if these break, networked play silently
    /// desyncs.
    /// </summary>
    public class MoveReplaySpec
    {
        private static readonly PlayerId Alice = new("alice");
        private static readonly PlayerId Bob = new("bob");

        private static MatchState DealFresh(ulong seed)
        {
            return Dealer.Deal(
                DealConfig.CutThroatDoubleSix(2),
                new[] { Alice, Bob },
                Partnership.CutThroat(new[] { Alice, Bob }),
                new SeededRandomSource(seed));
        }

        [Test]
        public void TwoDeals_SameSeed_SameMoves_ConvergeAfterPartialPlay()
        {
            CutThroatRules rules = new();
            const ulong seed = 0xDEADBEEFUL;

            MatchState s1 = DealFresh(seed);
            MatchState s2 = DealFresh(seed);

            List<Move> moves = new();
            for (int i = 0; i < 10 && !s1.IsOver; i++)
            {
                IReadOnlyList<Move> legal = rules.GetLegalMoves(s1);
                Move move = legal[0];
                moves.Add(move);
                s1 = rules.Apply(s1, move);
            }

            foreach (Move m in moves)
            {
                s2 = rules.Apply(s2, m);
            }

            AssertStatesConverged(s1, s2);
        }

        [Test]
        public void ReplayingFullMoveLog_FromSameSeed_ProducesSameEndState()
        {
            CutThroatRules rules = new();
            const ulong seed = 0xFEEDFACEUL;

            // Original play-through — drive it to completion with first-legal
            // moves so the sequence is fully deterministic.
            MatchState original = DealFresh(seed);
            List<Move> moves = new();
            while (!original.IsOver)
            {
                IReadOnlyList<Move> legal = rules.GetLegalMoves(original);
                Move move = legal[0];
                moves.Add(move);
                original = rules.Apply(original, move);
            }

            // Fresh deal + replay the move log. This is precisely the late-joiner
            // / reconnect path in M3.x — a client reconstructs the current state
            // by re-running the engine against the synced inputs.
            MatchState replayed = DealFresh(seed);
            foreach (Move m in moves)
            {
                replayed = rules.Apply(replayed, m);
            }

            Assert.That(replayed.IsOver, Is.True,
                "Replay must reach a terminal state.");
            AssertStatesConverged(original, replayed);
        }

        [Test]
        public void IsLegal_RejectsPlaceMove_WhenPlayerDoesNotHoldTile()
        {
            // Host-side validation in NetworkedMatch.RPC_SubmitMove leans on
            // IsLegal to refuse a corrupted move before appending it to the
            // networked log. If this guarantee ever weakened the host could
            // poison the move log with a tile no client actually held.
            CutThroatRules rules = new();
            MatchState s = DealFresh(0xCAFEBABEUL);

            Hand aliceHand = s.Hands[Alice];
            Tile? notHeld = FindAnyTileNotInHand(aliceHand);
            Assume.That(notHeld, Is.Not.Null,
                "Alice holds 14 of 28 tiles in 2P Cut-Throat; at least one tile must be missing.");

            PlaceMove illegal = new(Alice, notHeld!.Value, ChainEnd.Left);
            Assert.That(rules.IsLegal(s, illegal), Is.False);
        }

        [Test]
        public void IsLegal_RejectsMove_FromPlayerWhoseTurnItIsnt()
        {
            // Defensive: even a syntactically valid move from the wrong player
            // must be rejected. The networked RPC will be called by joiners
            // when it isn't their turn (e.g. an out-of-order or malicious
            // client) — IsLegal is the gate.
            CutThroatRules rules = new();
            MatchState s = DealFresh(0xC0FFEEUL);
            PlayerId outOfTurn = s.CurrentPlayer == Alice ? Bob : Alice;

            PassMove fromWrongPlayer = new(outOfTurn);
            Assert.That(rules.IsLegal(s, fromWrongPlayer), Is.False);
        }

        // ---- Helpers ------------------------------------------------------

        private static Tile? FindAnyTileNotInHand(Hand hand)
        {
            for (byte a = 0; a <= 6; a++)
            {
                for (byte b = a; b <= 6; b++)
                {
                    Tile t = new(a, b);
                    if (!hand.Contains(t))
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        private static void AssertStatesConverged(MatchState a, MatchState b)
        {
            Assert.That(b.IsOver, Is.EqualTo(a.IsOver), "IsOver diverged.");
            Assert.That(b.CurrentPlayerIndex, Is.EqualTo(a.CurrentPlayerIndex),
                "CurrentPlayerIndex diverged.");
            Assert.That(b.TurnNumber, Is.EqualTo(a.TurnNumber),
                "TurnNumber diverged.");
            Assert.That(b.ConsecutivePassCount, Is.EqualTo(a.ConsecutivePassCount),
                "ConsecutivePassCount diverged.");

            AssertChainEqual(a.Chain, b.Chain);
            AssertHandsEqual(a.Hands, b.Hands);
        }

        private static void AssertChainEqual(Chain a, Chain b)
        {
            Assert.That(b.Count, Is.EqualTo(a.Count), "Chain length diverged.");
            Assert.That(b.IsEmpty, Is.EqualTo(a.IsEmpty));
            if (!a.IsEmpty)
            {
                Assert.That(b.LeftEnd, Is.EqualTo(a.LeftEnd), "Chain LeftEnd diverged.");
                Assert.That(b.RightEnd, Is.EqualTo(a.RightEnd), "Chain RightEnd diverged.");
            }
            for (int i = 0; i < a.Count; i++)
            {
                PlacedTile ta = a.Tiles[i];
                PlacedTile tb = b.Tiles[i];
                Assert.That(tb.LeftPip, Is.EqualTo(ta.LeftPip),
                    $"Chain tile {i} LeftPip diverged.");
                Assert.That(tb.RightPip, Is.EqualTo(ta.RightPip),
                    $"Chain tile {i} RightPip diverged.");
            }
        }

        private static void AssertHandsEqual(
            IReadOnlyDictionary<PlayerId, Hand> a,
            IReadOnlyDictionary<PlayerId, Hand> b)
        {
            Assert.That(b.Count, Is.EqualTo(a.Count), "Hand-set size diverged.");
            foreach (KeyValuePair<PlayerId, Hand> kv in a)
            {
                Assert.That(b.ContainsKey(kv.Key), Is.True,
                    $"Player {kv.Key} missing in second state.");
                Hand handA = kv.Value;
                Hand handB = b[kv.Key];
                Assert.That(handB.Count, Is.EqualTo(handA.Count),
                    $"{kv.Key} hand size diverged.");

                List<Tile> listA = new(handA);
                List<Tile> listB = new(handB);
                for (int i = 0; i < listA.Count; i++)
                {
                    Assert.That(listB[i], Is.EqualTo(listA[i]),
                        $"{kv.Key} hand position {i} diverged.");
                }
            }
        }
    }
}
