#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    public class MatchStateTests
    {
        private static readonly PlayerId A = new("a");
        private static readonly PlayerId B = new("b");

        private static MatchState NewState()
        {
            Dictionary<PlayerId, Hand> hands = new()
            {
                [A] = new Hand(new[] { new Tile(0, 0) }),
                [B] = new Hand(new[] { new Tile(1, 1) }),
            };
            return new MatchState(
                players: new[] { A, B },
                partnership: Partnership.CutThroat(new[] { A, B }),
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
        public void Partnership_Is_Exposed_And_Resolves_Player_Teams()
        {
            // The MatchState carries the round's partnership so the rule engine can
            // look up "which team scores when this player wins?" without external
            // wiring. For Cut-Throat this resolves to each player's solo team.
            MatchState s = NewState();

            Assert.That(s.Partnership, Is.Not.Null);
            Assert.That(s.Partnership.GetTeamOf(A), Is.Not.EqualTo(s.Partnership.GetTeamOf(B)));
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
        public void With_Preserves_Unchanged_Fields_Including_Partnership()
        {
            MatchState original = NewState();

            MatchState modified = original.With(turnNumber: 42);

            Assert.That(modified.Players, Is.SameAs(original.Players));
            Assert.That(modified.Partnership, Is.SameAs(original.Partnership));
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
                partnership: Partnership.CutThroat(new[] { solo }),
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
            Dictionary<PlayerId, Hand> hands = new()
            {
                [A] = Hand.Empty,
                [B] = Hand.Empty,
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchState(
                players: new[] { A, B },
                partnership: Partnership.CutThroat(new[] { A, B }),
                currentPlayerIndex: 7,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false));
        }

        [Test]
        public void Constructor_Rejects_Partnership_That_Does_Not_Cover_Players_Set()
        {
            // Partnership covers {a, b, c} but match has only {a, b}. The integrity
            // invariant requires the two sets to match exactly so the rule engine can
            // always resolve GetTeamOf for any current player.
            PlayerId c = new("c");
            Dictionary<PlayerId, Hand> hands = new()
            {
                [A] = Hand.Empty,
                [B] = Hand.Empty,
            };

            Assert.Throws<ArgumentException>(() => new MatchState(
                players: new[] { A, B },
                partnership: Partnership.CutThroat(new[] { A, B, c }),
                currentPlayerIndex: 0,
                hands: hands,
                chain: Chain.Empty,
                turnNumber: 0,
                consecutivePassCount: 0,
                history: Array.Empty<Move>(),
                isOver: false));
        }
    }
}
