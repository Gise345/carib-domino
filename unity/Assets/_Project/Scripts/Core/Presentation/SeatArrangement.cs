#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Pure, Unity-free mapping from the seating problem — "N players in turn
    /// order, which one am I" — to the four fixed table seats. The local player
    /// always sits at <see cref="SeatPosition.Bottom"/>; the others are placed
    /// going around the table in turn order so the on-screen layout matches the
    /// order of play. Extracted from the renderer so the mapping is unit-tested
    /// (2/3/4 players, every local seat) rather than eyeballed on a device.
    /// </summary>
    public static class SeatArrangement
    {
        // Seat order by player count, indexed by turn-order offset from the
        // local player (offset 0 is always the local player = Bottom). Going
        // around: Bottom → Right → Top → Left.
        private static readonly SeatPosition[] TwoPlayer =
            { SeatPosition.Bottom, SeatPosition.Top };
        private static readonly SeatPosition[] ThreePlayer =
            { SeatPosition.Bottom, SeatPosition.Right, SeatPosition.Left };
        private static readonly SeatPosition[] FourPlayer =
            { SeatPosition.Bottom, SeatPosition.Right, SeatPosition.Top, SeatPosition.Left };

        /// <summary>
        /// Returns a seat per player, parallel to the match's player list: entry
        /// <c>p</c> is the seat for the player at index <c>p</c> in turn order.
        /// The player at <paramref name="localIndex"/> is always
        /// <see cref="SeatPosition.Bottom"/>.
        /// </summary>
        /// <param name="playerCount">Number of players, 2 to 4.</param>
        /// <param name="localIndex">The local player's index, in [0, playerCount).</param>
        public static SeatPosition[] Arrange(int playerCount, int localIndex)
        {
            SeatPosition[] order = playerCount switch
            {
                2 => TwoPlayer,
                3 => ThreePlayer,
                4 => FourPlayer,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(playerCount), playerCount, "Only 2, 3 or 4 players are supported."),
            };
            if (localIndex < 0 || localIndex >= playerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localIndex), localIndex, "Local index must be within the player count.");
            }

            SeatPosition[] seats = new SeatPosition[playerCount];
            for (int p = 0; p < playerCount; p++)
            {
                int offset = (p - localIndex + playerCount) % playerCount;
                seats[p] = order[offset];
            }
            return seats;
        }
    }
}
