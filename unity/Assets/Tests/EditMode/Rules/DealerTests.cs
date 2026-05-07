#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class DealerTests
    {
        private static readonly PlayerId Alice = new("alice");
        private static readonly PlayerId Bob = new("bob");
        private static readonly PlayerId Cara = new("cara");
        private static readonly PlayerId Dan = new("dan");

        [Test]
        public void Deal_Propagates_Input_Partnership_Onto_The_New_State()
        {
            // The Dealer is the only place a fresh MatchState is constructed, so the
            // partnership it receives must round-trip onto state.Partnership unchanged.
            // The rule engine downstream relies on this for GetTeamOf lookups.
            Partnership partnership = Partnership.AlternatingPairs(Alice, Bob, Cara, Dan);

            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                partnership,
                new SeededRandomSource(42));

            Assert.That(state.Partnership, Is.SameAs(partnership));
            Assert.That(state.Partnership.GetTeamOf(Alice), Is.EqualTo(state.Partnership.GetTeamOf(Cara)));
            Assert.That(state.Partnership.GetTeamOf(Bob), Is.EqualTo(state.Partnership.GetTeamOf(Dan)));
        }

        [Test]
        public void Two_Player_CutThroat_Deal_Gives_14_Tiles_Each()
        {
            // 2-player Jamaican Cut-Throat splits the full 28-tile set evenly: 14
            // each, no sleeping tiles. Replaces the old (incorrect) 7-each behaviour
            // inherited from generic Block dominoes.
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(2),
                new[] { Alice, Bob },
                Partnership.CutThroat(new[] { Alice, Bob }),
                new SeededRandomSource(42));

            Assert.That(state.Hands[Alice].Count, Is.EqualTo(14));
            Assert.That(state.Hands[Bob].Count, Is.EqualTo(14));
        }

        [Test]
        public void Four_Player_CutThroat_Deal_Distributes_All_28_Tiles()
        {
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                Partnership.CutThroat(new[] { Alice, Bob, Cara, Dan }),
                new SeededRandomSource(42));

            int totalTiles = 0;
            foreach (Hand h in state.Hands.Values)
            {
                totalTiles += h.Count;
            }
            Assert.That(totalTiles, Is.EqualTo(28));
        }

        [Test]
        public void Initial_State_Has_Empty_Chain_And_No_History()
        {
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(2),
                new[] { Alice, Bob },
                Partnership.CutThroat(new[] { Alice, Bob }),
                new SeededRandomSource(42));

            Assert.That(state.Chain.IsEmpty, Is.True);
            Assert.That(state.History.Count, Is.EqualTo(0));
            Assert.That(state.TurnNumber, Is.EqualTo(0));
            Assert.That(state.ConsecutivePassCount, Is.EqualTo(0));
            Assert.That(state.IsOver, Is.False);
        }

        [Test]
        public void Same_Seed_Produces_Identical_Deal()
        {
            // Replay-determinism foundation: same seed → same observable initial state.
            // The eventual M4 settlement validator will rely on this exact property
            // when it replays a match log against the server-issued seed.
            MatchState first = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                Partnership.CutThroat(new[] { Alice, Bob, Cara, Dan }),
                new SeededRandomSource(0xCAFEBABEUL));

            MatchState second = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                Partnership.CutThroat(new[] { Alice, Bob, Cara, Dan }),
                new SeededRandomSource(0xCAFEBABEUL));

            for (int i = 0; i < 4; i++)
            {
                PlayerId p = first.Players[i];
                CollectionAssert.AreEqual(first.Hands[p], second.Hands[p],
                    $"Player {p} hand differs.");
            }
            Assert.That(first.CurrentPlayerIndex, Is.EqualTo(second.CurrentPlayerIndex));
        }

        [Test]
        public void Different_Seeds_Produce_Different_Deals()
        {
            MatchState a = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(2),
                new[] { Alice, Bob },
                Partnership.CutThroat(new[] { Alice, Bob }),
                new SeededRandomSource(1));
            MatchState b = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(2),
                new[] { Alice, Bob },
                Partnership.CutThroat(new[] { Alice, Bob }),
                new SeededRandomSource(2));

            // Almost certainly distinct hands; check at least one tile differs.
            HashSet<Tile> aliceA = new(a.Hands[Alice]);
            HashSet<Tile> aliceB = new(b.Hands[Alice]);
            Assert.That(aliceA.SetEquals(aliceB), Is.False);
        }

        [Test]
        public void Dealt_Tiles_Are_Disjoint_Across_Players()
        {
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                Partnership.CutThroat(new[] { Alice, Bob, Cara, Dan }),
                new SeededRandomSource(0xDEADBEEFUL));

            HashSet<Tile> seen = new();
            foreach (Hand h in state.Hands.Values)
            {
                foreach (Tile t in h)
                {
                    Assert.That(seen.Add(t), Is.True, $"Tile {t} dealt twice.");
                }
            }
        }

        [Test]
        public void Starting_Player_Holds_The_Highest_Double_Or_Highest_Single()
        {
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                new[] { Alice, Bob, Cara, Dan },
                Partnership.CutThroat(new[] { Alice, Bob, Cara, Dan }),
                new SeededRandomSource(0xDEADBEEFUL));

            Hand starterHand = state.Hands[state.CurrentPlayer];

            // Either the starter holds the highest double held by anyone, or no
            // double is held by anyone and the starter holds the highest single.
            byte? highestDoubleAnywhere = null;
            foreach (Hand h in state.Hands.Values)
            {
                foreach (Tile t in h)
                {
                    if (t.IsDouble && (highestDoubleAnywhere == null || t.A > highestDoubleAnywhere))
                    {
                        highestDoubleAnywhere = t.A;
                    }
                }
            }

            if (highestDoubleAnywhere.HasValue)
            {
                Tile expected = new(highestDoubleAnywhere.Value, highestDoubleAnywhere.Value);
                Assert.That(starterHand.Contains(expected), Is.True,
                    $"Starter {state.CurrentPlayer} should hold {expected}.");
            }
            else
            {
                int highestPipsAnywhere = 0;
                foreach (Hand h in state.Hands.Values)
                {
                    foreach (Tile t in h)
                    {
                        if (t.Pips > highestPipsAnywhere)
                        {
                            highestPipsAnywhere = t.Pips;
                        }
                    }
                }

                bool found = false;
                foreach (Tile t in starterHand)
                {
                    if (t.Pips == highestPipsAnywhere)
                    {
                        found = true;
                        break;
                    }
                }
                Assert.That(found, Is.True);
            }
        }
    }
}
