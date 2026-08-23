#nullable enable
using System;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// The pot must match what the server would compute, and the constants must
    /// match <c>functions/src/lib/economy.ts</c>. The constant assertions look
    /// tautological in isolation — they are not. They are the tripwire for the
    /// one failure this mirror can have: someone changes the stake on the
    /// server and the client keeps quoting the old number to players.
    /// </summary>
    public class StakesTests
    {
        [Test]
        public void Constants_MatchTheServerEconomy()
        {
            Assert.That(Stakes.EntryStake, Is.EqualTo(1000), "ENTRY_STAKE in economy.ts");
            Assert.That(Stakes.KeyBonus, Is.EqualTo(2000), "KEY_BONUS in economy.ts");
        }

        [Test]
        public void PotFor_IsEntryTimesSeats([Values(2, 3, 4)] int seats)
        {
            int pot = Stakes.PotFor(seats);

            Assert.That(pot, Is.EqualTo(Stakes.EntryStake * seats));
        }

        [Test]
        public void PotFor_ThreeHandTable_IsThreeThousand()
        {
            Assert.That(Stakes.PotFor(3), Is.EqualTo(3000));
        }

        [Test]
        public void PotFor_RejectsTablesThatCannotBeDealt([Values(-1, 0, 1, 5, 99)] int seats)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Stakes.PotFor(seats));
        }

        [Test]
        public void ShareOf_SoloWinnerTakesTheWholePot()
        {
            Assert.That(Stakes.ShareOf(4000, winners: 1), Is.EqualTo(4000));
        }

        [Test]
        public void ShareOf_PartnersSplitEvenly()
        {
            Assert.That(Stakes.ShareOf(4000, winners: 2), Is.EqualTo(2000));
        }

        [Test]
        public void ShareOf_RejectsNoWinners([Values(0, -1)] int winners)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Stakes.ShareOf(4000, winners));
        }
    }
}
