#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// A match series awards 1000 + a game per round win, ends at the format
    /// target, and models the cut-throat battle: a lead tie forces a battle whose
    /// loser is wiped back to love.
    /// </summary>
    public class SeriesStateTests
    {
        private static readonly PlayerId A = new("alice");
        private static readonly PlayerId B = new("bob");
        private static readonly PlayerId C = new("cara");

        private static readonly IReadOnlyList<PlayerId> Duo = new[] { A, B };
        private static readonly IReadOnlyList<PlayerId> Trio = new[] { A, B, C };

        private static MatchOutcome Win(PlayerId winner) => new(
            MatchEndReason.Domino, winner, null, 50, new Dictionary<PlayerId, int>());

        [Test]
        public void ApplyRound_WinnerGainsGameAnd1000()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.ClassicSixLove).ApplyRound(Win(A));

            Assert.That(s.PointsOf(A), Is.EqualTo(1000));
            Assert.That(s.GamesWonBy(A), Is.EqualTo(1));
            Assert.That(s.RoundsPlayed, Is.EqualTo(1));
        }

        [Test]
        public void Classic_OverAt6000()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.ClassicSixLove);
            for (int i = 0; i < 6; i++)
            {
                s = s.ApplyRound(Win(A));
            }
            Assert.That(s.PointsOf(A), Is.EqualTo(6000));
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(A));
        }

        [Test]
        public void Quick_OverAt3000()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.QuickLove)
                .ApplyRound(Win(A)).ApplyRound(Win(A));
            Assert.That(s.IsOver, Is.False);

            s = s.ApplyRound(Win(A));
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(A));
        }

        [Test]
        public void LeadTie_FlagsAPendingBattleBetweenTheTiedPair()
        {
            // A wins, then B wins → both on 1 game → next round is a battle.
            SeriesState s = SeriesState.New(Duo, MatchFormat.ClassicSixLove)
                .ApplyRound(Win(A)).ApplyRound(Win(B));

            Assert.That(s.PendingBattle, Is.True);
            Assert.That(s.BattlePlayers, Is.EquivalentTo(new[] { A, B }));
        }

        [Test]
        public void Battle_WonByABattler_ResetsTheOtherToLove()
        {
            SeriesState s = SeriesState.New(Duo, MatchFormat.ClassicSixLove)
                .ApplyRound(Win(A)).ApplyRound(Win(B)); // 1-1, battle pending
            Assert.That(s.PendingBattle, Is.True);

            s = s.ApplyRound(Win(A)); // A wins the battle → B wiped to love

            Assert.That(s.GamesWonBy(A), Is.EqualTo(2));
            Assert.That(s.PointsOf(A), Is.EqualTo(2000));
            Assert.That(s.GamesWonBy(B), Is.EqualTo(0));
            Assert.That(s.PointsOf(B), Is.EqualTo(0));
            Assert.That(s.PendingBattle, Is.False);
        }

        [Test]
        public void Battle_WonByOutsider_KeepsTheTieAlive()
        {
            // A and B tie on 1; C wins the battle round → A & B still tied on 1,
            // C now also on 1 → three-way tie persists.
            SeriesState s = SeriesState.New(Trio, MatchFormat.ClassicSixLove)
                .ApplyRound(Win(A)).ApplyRound(Win(B)); // A=1,B=1 → battle
            s = s.ApplyRound(Win(C));

            Assert.That(s.GamesWonBy(A), Is.EqualTo(1));
            Assert.That(s.GamesWonBy(B), Is.EqualTo(1));
            Assert.That(s.GamesWonBy(C), Is.EqualTo(1));
            Assert.That(s.PendingBattle, Is.True);
            Assert.That(s.BattlePlayers, Is.EquivalentTo(new[] { A, B, C }));
        }
    }
}
