#nullable enable
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// The move-cursor rebase these tests pin is the difference between a
    /// rematch that replays cleanly on both clients and one that silently
    /// desyncs. See <see cref="MatchSignalTracker"/>.
    /// </summary>
    public class MatchSignalTrackerTests
    {
        [Test]
        public void Observe_BeforeDealReady_ReportsNothing()
        {
            MatchSignalTracker tracker = new();

            MatchSignal signal = tracker.Observe(dealReady: false, roundNumber: 0, moveCount: 0);

            Assert.That(signal.DealStarted, Is.False);
            Assert.That(signal.RoundStarted, Is.False);
            Assert.That(signal.HasMoves, Is.False);
        }

        [Test]
        public void Observe_FirstDealReady_ReportsDealStartedWithoutMoves()
        {
            MatchSignalTracker tracker = new();

            MatchSignal signal = tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);

            Assert.That(signal.DealStarted, Is.True);
            Assert.That(signal.RoundStarted, Is.False);
            Assert.That(signal.HasMoves, Is.False);
        }

        [Test]
        public void Observe_RepeatedAfterDeal_ReportsDealStartedExactlyOnce()
        {
            MatchSignalTracker tracker = new();
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);

            MatchSignal second = tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);
            MatchSignal third = tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);

            Assert.That(second.DealStarted, Is.False);
            Assert.That(third.DealStarted, Is.False);
        }

        [Test]
        public void Observe_MoveCountAdvances_ReportsHalfOpenRange()
        {
            MatchSignalTracker tracker = new();
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);

            MatchSignal first = tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 3);
            MatchSignal second = tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 5);

            Assert.That(first.MovesFrom, Is.EqualTo(0));
            Assert.That(first.MovesTo, Is.EqualTo(3));
            Assert.That(second.MovesFrom, Is.EqualTo(3));
            Assert.That(second.MovesTo, Is.EqualTo(5));
        }

        [Test]
        public void Observe_RoundAdvancesWithMoveCountReset_ReportsRoundStartedWithoutMoves()
        {
            MatchSignalTracker tracker = new();
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 14);

            MatchSignal signal = tracker.Observe(dealReady: true, roundNumber: 2, moveCount: 0);

            Assert.That(signal.RoundStarted, Is.True);
            Assert.That(signal.DealStarted, Is.False);
            Assert.That(signal.HasMoves, Is.False);
        }

        [Test]
        public void Observe_FirstMoveAfterRematch_ReplaysFromIndexZero()
        {
            MatchSignalTracker tracker = new();
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 14);
            tracker.Observe(dealReady: true, roundNumber: 2, moveCount: 0);

            MatchSignal signal = tracker.Observe(dealReady: true, roundNumber: 2, moveCount: 1);

            Assert.That(signal.MovesFrom, Is.EqualTo(0));
            Assert.That(signal.MovesTo, Is.EqualTo(1));
        }

        [Test]
        public void Observe_RoundAdvancesWithMovesAlreadyAppended_ReportsBothInOneSignal()
        {
            MatchSignalTracker tracker = new();
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 14);

            MatchSignal signal = tracker.Observe(dealReady: true, roundNumber: 2, moveCount: 2);

            Assert.That(signal.RoundStarted, Is.True);
            Assert.That(signal.MovesFrom, Is.EqualTo(0));
            Assert.That(signal.MovesTo, Is.EqualTo(2));
        }

        [Test]
        public void Observe_FirstDealOnLateSnapshot_ReplaysExistingMovesFromZero()
        {
            MatchSignalTracker tracker = new();

            MatchSignal signal = tracker.Observe(dealReady: true, roundNumber: 3, moveCount: 4);

            Assert.That(signal.DealStarted, Is.True);
            Assert.That(signal.RoundStarted, Is.False);
            Assert.That(signal.MovesFrom, Is.EqualTo(0));
            Assert.That(signal.MovesTo, Is.EqualTo(4));
        }

        [Test]
        public void Observe_MoveCountRewindsWithoutRoundChange_ReportsNoMoves()
        {
            MatchSignalTracker tracker = new();
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 6);

            MatchSignal signal = tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 2);

            Assert.That(signal.HasMoves, Is.False);
            Assert.That(signal.RoundStarted, Is.False);
        }

        [Test]
        public void Observe_AfterRewind_DoesNotReplaySwallowedMoves()
        {
            MatchSignalTracker tracker = new();
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 0);
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 6);
            tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 2);

            MatchSignal signal = tracker.Observe(dealReady: true, roundNumber: 1, moveCount: 7);

            Assert.That(signal.MovesFrom, Is.EqualTo(6));
            Assert.That(signal.MovesTo, Is.EqualTo(7));
        }
    }
}
