#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// A match series awards a flat 1000 per round win, ends Classic at 6000 and
    /// Quick after six rounds, and only names a winner once there is a sole
    /// leader (a Quick tie stays alive for sudden death).
    /// </summary>
    public class SeriesStateTests
    {
        private static readonly PlayerId A = new("alice");
        private static readonly PlayerId B = new("bob");
        private static readonly PlayerId C = new("cara");

        private static readonly IReadOnlyList<PlayerId> Trio = new[] { A, B, C };

        private static MatchOutcome Win(PlayerId winner) => new(
            MatchEndReason.Domino, winner, null, 50, new Dictionary<PlayerId, int>());

        private static MatchOutcome Draw() => new(
            MatchEndReason.Blocked, null, null, 0, new Dictionary<PlayerId, int>());

        [Test]
        public void New_EveryoneStartsAtZero()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.ClassicSixLove);

            Assert.That(s.RoundsPlayed, Is.EqualTo(0));
            Assert.That(s.PointsOf(A), Is.EqualTo(0));
            Assert.That(s.IsOver, Is.False);
            Assert.That(s.Winner, Is.Null);
        }

        [Test]
        public void ApplyRound_WinnerGains1000_AndAdvancesRound()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.ClassicSixLove).ApplyRound(Win(A));

            Assert.That(s.PointsOf(A), Is.EqualTo(1000));
            Assert.That(s.PointsOf(B), Is.EqualTo(0));
            Assert.That(s.RoundsPlayed, Is.EqualTo(1));
        }

        [Test]
        public void ApplyRound_DrawAwardsNobody_ButStillCountsTheRound()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.QuickSixRounds).ApplyRound(Draw());

            Assert.That(s.PointsOf(A), Is.EqualTo(0));
            Assert.That(s.RoundsPlayed, Is.EqualTo(1));
        }

        [Test]
        public void Classic_OverWhenAPlayerReaches6000()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.ClassicSixLove);
            for (int i = 0; i < 5; i++)
            {
                s = s.ApplyRound(Win(A));
                Assert.That(s.IsOver, Is.False, $"after {i + 1} wins");
            }

            s = s.ApplyRound(Win(A)); // 6th → 6000

            Assert.That(s.PointsOf(A), Is.EqualTo(6000));
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(A));
        }

        [Test]
        public void Quick_OverAfterSixRoundsWithSoleLeader()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.QuickSixRounds);
            s = s.ApplyRound(Win(A)).ApplyRound(Win(A)).ApplyRound(Win(A)); // A=3000
            s = s.ApplyRound(Win(B)).ApplyRound(Win(B)).ApplyRound(Win(C)); // B=2000, C=1000

            Assert.That(s.RoundsPlayed, Is.EqualTo(6));
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(A));
        }

        [Test]
        public void Quick_TieAtSixRounds_StaysAliveForSuddenDeath()
        {
            SeriesState s = SeriesState.New(Trio, MatchFormat.QuickSixRounds);
            // A and B both finish on 3000 after six rounds.
            s = s.ApplyRound(Win(A)).ApplyRound(Win(A)).ApplyRound(Win(A));
            s = s.ApplyRound(Win(B)).ApplyRound(Win(B)).ApplyRound(Win(B));

            Assert.That(s.RoundsPlayed, Is.EqualTo(6));
            Assert.That(s.IsOver, Is.False, "a tie must not end the match");
            Assert.That(s.Winner, Is.Null);

            // Sudden-death round breaks the tie.
            s = s.ApplyRound(Win(A));
            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Winner, Is.EqualTo(A));
        }
    }
}
