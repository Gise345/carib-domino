#nullable enable
using System;
using System.Linq;
using NUnit.Framework;

namespace Pose.Core.Tests
{
    /// <summary>
    /// The local player must always sit at the bottom, every player must get a
    /// distinct seat, and the others must be placed in turn order — for 2, 3 and
    /// 4 players and from any local seat.
    /// </summary>
    public class SeatArrangementTests
    {
        [Test]
        public void Arrange_LocalPlayerAlwaysBottom([Values(2, 3, 4)] int count)
        {
            for (int local = 0; local < count; local++)
            {
                SeatPosition[] seats = SeatArrangement.Arrange(count, local);
                Assert.That(seats[local], Is.EqualTo(SeatPosition.Bottom), $"count={count} local={local}");
            }
        }

        [Test]
        public void Arrange_AllSeatsDistinct([Values(2, 3, 4)] int count)
        {
            for (int local = 0; local < count; local++)
            {
                SeatPosition[] seats = SeatArrangement.Arrange(count, local);
                Assert.That(seats.Distinct().Count(), Is.EqualTo(count), $"count={count} local={local}");
            }
        }

        [Test]
        public void Arrange_TwoPlayers_LocalBottomOpponentTop()
        {
            SeatPosition[] seats = SeatArrangement.Arrange(2, localIndex: 0);

            Assert.That(seats[0], Is.EqualTo(SeatPosition.Bottom));
            Assert.That(seats[1], Is.EqualTo(SeatPosition.Top));
        }

        [Test]
        public void Arrange_ThreePlayers_FromLocalOne_SeatsInTurnOrder()
        {
            // local=1 → offset0=player1 (Bottom), offset1=player2 (Right),
            // offset2=player0 (Left).
            SeatPosition[] seats = SeatArrangement.Arrange(3, localIndex: 1);

            Assert.That(seats[1], Is.EqualTo(SeatPosition.Bottom));
            Assert.That(seats[2], Is.EqualTo(SeatPosition.Right));
            Assert.That(seats[0], Is.EqualTo(SeatPosition.Left));
        }

        [Test]
        public void Arrange_FourPlayers_FromLocalTwo_SeatsInTurnOrder()
        {
            // local=2 → offsets: player2=Bottom, player3=Right, player0=Top,
            // player1=Left.
            SeatPosition[] seats = SeatArrangement.Arrange(4, localIndex: 2);

            Assert.That(seats[2], Is.EqualTo(SeatPosition.Bottom));
            Assert.That(seats[3], Is.EqualTo(SeatPosition.Right));
            Assert.That(seats[0], Is.EqualTo(SeatPosition.Top));
            Assert.That(seats[1], Is.EqualTo(SeatPosition.Left));
        }

        [Test]
        public void Arrange_NextPlayerAfterLocalIsAlwaysAdjacent([Values(2, 3, 4)] int count)
        {
            // The player who plays immediately after the local player should sit
            // to the right (or top, in 2P) — never skipped past a further seat.
            for (int local = 0; local < count; local++)
            {
                SeatPosition[] seats = SeatArrangement.Arrange(count, local);
                int next = (local + 1) % count;
                SeatPosition expected = count == 2 ? SeatPosition.Top : SeatPosition.Right;
                Assert.That(seats[next], Is.EqualTo(expected), $"count={count} local={local}");
            }
        }

        [Test]
        public void Arrange_InvalidCount_Throws([Values(1, 5)] int count)
        {
            Assert.That(
                () => SeatArrangement.Arrange(count, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Arrange_LocalIndexOutOfRange_Throws()
        {
            Assert.That(
                () => SeatArrangement.Arrange(3, localIndex: 3),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
