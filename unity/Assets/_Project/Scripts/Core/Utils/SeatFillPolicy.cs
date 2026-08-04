#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Decides how a table copes with empty or vacated seats, so online play
    /// never stalls on a missing player and no seat is a privileged "host". Pure
    /// and unit-tested; the networked layer (<c>NetworkedMatch</c> /
    /// <c>OnlineMatchController</c>) applies the decision. Two moments:
    /// <list type="bullet">
    ///   <item><b>At the auto-start deadline</b> — fill every unoccupied seat with
    ///         a bot so the table can deal immediately
    ///         (<see cref="BotsToFillAtStart"/>).</item>
    ///   <item><b>When a player leaves mid-round</b> — if two or more humans
    ///         remain, replace the leaver with a bot and play on; if one or none
    ///         remain, end the round (<see cref="OnLeave"/>).</item>
    /// </list>
    /// </summary>
    public static class SeatFillPolicy
    {
        /// <summary>The minimum humans a round needs to keep going online.</summary>
        public const int MinHumans = 2;

        /// <summary>What to do when a player leaves an in-progress round.</summary>
        public enum LeaveAction
        {
            /// <summary>Replace the departed seat(s) with bots and continue.</summary>
            FillWithBots,

            /// <summary>Too few humans left — end the round (the lone human wins).</summary>
            EndRound,
        }

        /// <summary>
        /// Number of bot seats to add at the auto-start deadline to fill the
        /// table. Any seat not held by a present human becomes a bot, so the round
        /// always deals a full <paramref name="tableSize"/>.
        /// </summary>
        /// <param name="occupiedByHumans">Seats currently held by present humans (≥1).</param>
        /// <param name="tableSize">The table's target size, 2–4.</param>
        public static int BotsToFillAtStart(int occupiedByHumans, int tableSize)
        {
            if (tableSize < 2 || tableSize > MaxTable)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableSize), tableSize, "Table size must be 2, 3, or 4.");
            }
            if (occupiedByHumans < 1 || occupiedByHumans > tableSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(occupiedByHumans), occupiedByHumans,
                    "Occupied seats must be between 1 and the table size.");
            }

            return tableSize - occupiedByHumans;
        }

        /// <summary>
        /// Whether a mid-round departure should be bot-filled (play on) or end the
        /// round. Two or more humans left → play on with bots; otherwise end. This
        /// makes a 2-player leave end the round (the remaining player wins) while a
        /// 3-/4-player leave keeps the table alive.
        /// </summary>
        /// <param name="humansRemaining">Humans still present after the departure.</param>
        public static LeaveAction OnLeave(int humansRemaining)
        {
            return humansRemaining >= MinHumans ? LeaveAction.FillWithBots : LeaveAction.EndRound;
        }

        private const int MaxTable = 4;
    }
}
