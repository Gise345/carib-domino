#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// The table must never stall on a missing player: empty seats fill with bots
    /// at start, a mid-round leave in a 3-/4-player game plays on with a bot, and
    /// a leave that drops a table to one human ends the round.
    /// </summary>
    public class SeatFillPolicyTests
    {
        [Test]
        public void BotsToFillAtStart_FillsToTable([Values(2, 3, 4)] int tableSize)
        {
            for (int humans = 1; humans <= tableSize; humans++)
            {
                Assert.That(
                    SeatFillPolicy.BotsToFillAtStart(humans, tableSize),
                    Is.EqualTo(tableSize - humans),
                    $"table={tableSize} humans={humans}");
            }
        }

        [Test]
        public void BotsToFillAtStart_SoloPlayerGetsAFullTableOfBots()
        {
            Assert.That(SeatFillPolicy.BotsToFillAtStart(1, 4), Is.EqualTo(3));
        }

        [Test]
        public void BotsToFillAtStart_RejectsBadTableSize([Values(0, 1, 5)] int tableSize)
        {
            Assert.That(
                () => SeatFillPolicy.BotsToFillAtStart(1, tableSize),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void BotsToFillAtStart_RejectsBadOccupancy([Values(0, 5)] int humans)
        {
            Assert.That(
                () => SeatFillPolicy.BotsToFillAtStart(humans, 4),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void OnLeave_TwoOrMoreHumansRemaining_FillsWithBots([Values(2, 3)] int remaining)
        {
            Assert.That(
                SeatFillPolicy.OnLeave(remaining),
                Is.EqualTo(SeatFillPolicy.LeaveAction.FillWithBots));
        }

        [Test]
        public void OnLeave_OneOrNoneRemaining_EndsRound([Values(0, 1)] int remaining)
        {
            Assert.That(
                SeatFillPolicy.OnLeave(remaining),
                Is.EqualTo(SeatFillPolicy.LeaveAction.EndRound));
        }

        [Test]
        public void OnLeave_TwoPlayerLeave_EndsRound()
        {
            // A 2-player table where the opponent leaves: one human remains → end
            // (the remaining player wins), rather than play on against a bot.
            Assert.That(
                SeatFillPolicy.OnLeave(1),
                Is.EqualTo(SeatFillPolicy.LeaveAction.EndRound));
        }
    }
}
