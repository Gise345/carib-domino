#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Everything a game-room screen states as fact before you commit: how many
    /// seats, how long the series runs, how long a turn lasts, and what the
    /// table pays.
    ///
    /// It exists so those numbers are derived rather than typed. The room
    /// screens used to carry sentences like "winner takes the pot + 2,000 key
    /// bonus", which is a copy of the rules that can drift from them; here the
    /// series length comes from <see cref="MatchFormatRules"/>, the clock from
    /// <see cref="TurnTimer"/>, and the money from <see cref="Stakes"/>. Change
    /// a rule and the lobby follows on its own.
    ///
    /// Pure and allocation-light — one of these is built per selection change,
    /// not per frame.
    /// </summary>
    public sealed class RoomSummary
    {
        /// <summary>Seats at this table.</summary>
        public int Seats { get; }

        /// <summary>The mode this table plays.</summary>
        public GameMode Mode { get; }

        /// <summary>The series format this table plays.</summary>
        public MatchFormat Format { get; }

        /// <summary>Coins each seat stakes to enter.</summary>
        public int Entry { get; }

        /// <summary>The whole staked pot.</summary>
        public int Pot { get; }

        /// <summary>How many players share a win — 1 solo, 2 partners.</summary>
        public int Winners { get; }

        /// <summary>
        /// What the winning side collectively takes: the whole pot. For Partner
        /// this is the team's take, which is then split between the partners.
        /// </summary>
        public int WinningSideTakes { get; }

        /// <summary>What one winner receives from the pot, before key bonuses.</summary>
        public int ShareEach { get; }

        /// <summary>Minted bonus a key adds on top.</summary>
        public int KeyBonus { get; }

        /// <summary>Round wins ("loves") that end the series.</summary>
        public int Loves { get; }

        /// <summary>Seconds a player has to make their move.</summary>
        public int TurnSeconds { get; }

        /// <summary>Whether the winning side is a partnership rather than one player.</summary>
        public bool IsTeamGame => Winners > 1;

        private RoomSummary(GameMode mode, int seats, MatchFormat format)
        {
            Mode = mode;
            Seats = seats;
            Format = format;

            Entry = Stakes.EntryStake;
            Pot = Stakes.PotFor(seats);
            Winners = mode == GameMode.Partner ? 2 : 1;
            WinningSideTakes = Pot;
            ShareEach = Stakes.ShareOf(Pot, Winners);
            KeyBonus = Stakes.KeyBonus;

            Loves = MatchFormatRules.For(format).Loves;
            TurnSeconds = (int)TurnTimer.ExpireAfterSeconds;
        }

        /// <summary>
        /// Builds the summary for a table. Partner is always a full four seats,
        /// so the seat count is ignored for that mode rather than trusted — a
        /// two-seat Partner table is not a thing the rules can express.
        /// </summary>
        /// <param name="mode">Cut-Throat or Partner.</param>
        /// <param name="seats">Seats at the table, 2..4. Ignored for Partner.</param>
        /// <param name="format">The series format.</param>
        /// <returns>The numbers this room should state.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Cut-Throat seats outside 2..4.</exception>
        public static RoomSummary For(GameMode mode, int seats, MatchFormat format)
        {
            int actualSeats = mode == GameMode.Partner ? Stakes.MaxSeats : seats;
            return new RoomSummary(mode, actualSeats, format);
        }
    }
}
