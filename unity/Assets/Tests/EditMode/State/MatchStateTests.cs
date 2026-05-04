#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class MatchStateTests
    {
        private static MatchState NewState()
        {
            PlayerId a = new("a");
            PlayerId b = new("b");
            Dictionary<PlayerId, Hand> hands = new()
            {
                [a] = new Hand(new[] { new Tile(0, 0) }),
                [b] = new Hand(new[] { new Tile(1, 1) }),
            };
            return new MatchState(
                players: new[] { a, b },
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false);
        }

        [Test]
        public void CurrentPlayer_Resolves_From_Players_And_Index()
        {
            MatchState s = NewState();

            Assert.That(s.CurrentPlayer.Value, Is.EqualTo("a"));
        }

        [Test]
        public void With_Returns_New_Instance()
        {
            MatchState original = NewState();

            MatchState modified = original.With(turnNumber: 5);

            Assert.That(modified, Is.Not.SameAs(original));
            Assert.That(modified.TurnNumber, Is.EqualTo(5));
            Assert.That(original.TurnNumber, Is.EqualTo(0));
        }

        [Test]
        public void With_Preserves_Unchanged_Fields()
        {
            MatchState original = NewState();

            MatchState modified = original.With(turnNumber: 42);

            Assert.That(modified.Players, Is.SameAs(original.Players));
            Assert.That(modified.Hands, Is.SameAs(original.Hands));
            Assert.That(modified.Chain, Is.SameAs(original.Chain));
            Assert.That(modified.CurrentPlayerIndex, Is.EqualTo(original.CurrentPlayerIndex));
        }

        [Test]
        public void Constructor_Rejects_Single_Player()
        {
            PlayerId solo = new("solo");
            Dictionary<PlayerId, Hand> hands = new() { [solo] = Hand.Empty };

            Assert.Throws<ArgumentException>(() => new MatchState(
                players: new[] { solo },
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false));
        }

        [Test]
        public void Constructor_Rejects_Out_Of_Range_CurrentPlayerIndex()
        {
            PlayerId a = new("a");
            PlayerId b = new("b");
            Dictionary<PlayerId, Hand> hands = new()
            {
                [a] = Hand.Empty,
                [b] = Hand.Empty,
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchState(
                players: new[] { a, b },
                currentPlayerIndex: 7,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false));
        }
    }
}
