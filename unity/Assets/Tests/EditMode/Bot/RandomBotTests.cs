#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class RandomBotTests
    {
        private static readonly PlayerId Alice = new("alice");
        private static readonly PlayerId Bob = new("bob");

        // ---- Helpers --------------------------------------------------------

        /// <summary>
        /// A throwaway 2-player MatchState. RandomBot does not consult state
        /// fields, so we don't bother making it represent a meaningful round.
        /// </summary>
        private static MatchState DummyState()
        {
            Dictionary<PlayerId, Hand> hands = new()
            {
                [Alice] = Hand.Empty,
                [Bob] = Hand.Empty,
            };
            return new MatchState(
                players: new[] { Alice, Bob },
                partnership: Partnership.CutThroat(new[] { Alice, Bob }),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false);
        }

        // ---- Tests ----------------------------------------------------------

        [Test]
        public void Picks_A_Move_From_The_Legal_List()
        {
            RandomBot bot = new();
            PassMove pass = new(Alice);
            PlaceMove place = new(Alice, new Tile(0, 0), ChainEnd.Left);
            Move[] legal = { pass, place };

            Move picked = bot.PickMove(DummyState(), legal, new SeededRandomSource(42UL));

            Assert.That(picked, Is.SameAs(pass).Or.SameAs(place));
        }

        [Test]
        public void Same_Seed_And_Same_Legal_List_Yields_Same_Pick()
        {
            // Replay-determinism foundation: server-side validator must be able
            // to reproduce a bot's choices given the bot's input seed.
            RandomBot bot = new();
            Move[] legal =
            {
                new PassMove(Alice),
                new PlaceMove(Alice, new Tile(0, 0), ChainEnd.Left),
                new PlaceMove(Alice, new Tile(1, 1), ChainEnd.Right),
            };

            Move first = bot.PickMove(DummyState(), legal, new SeededRandomSource(0xCAFEBABEUL));
            Move second = bot.PickMove(DummyState(), legal, new SeededRandomSource(0xCAFEBABEUL));

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Throws_On_Empty_Legal_List()
        {
            RandomBot bot = new();

            Assert.Throws<ArgumentException>(() =>
                bot.PickMove(DummyState(), Array.Empty<Move>(), new SeededRandomSource(1UL)));
        }

        [Test]
        public void Over_Many_Iterations_Every_Move_Gets_Picked_At_Least_Once()
        {
            // Distribution sanity: with 3 moves and 100 picks from a single RNG
            // sequence, every option should appear at least once. Using one RNG
            // across the loop tests the bot's interaction with NextInt — a buggy
            // implementation that always returned 0 would still satisfy the
            // single-call tests above.
            RandomBot bot = new();
            Move[] legal =
            {
                new PassMove(Alice),
                new PlaceMove(Alice, new Tile(0, 0), ChainEnd.Left),
                new PlaceMove(Alice, new Tile(1, 1), ChainEnd.Right),
            };

            SeededRandomSource rng = new(0xCAFEBABEUL);
            HashSet<Move> distinct = new(); // Move has reference equality (no Equals override).
            for (int i = 0; i < 100; i++)
            {
                distinct.Add(bot.PickMove(DummyState(), legal, rng));
            }

            Assert.That(distinct.Count, Is.EqualTo(3),
                "All three moves should be picked at least once across 100 iterations.");
        }
    }
}
