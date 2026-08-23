#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// The coin numbers a room screen shows before you sit down — entry, pot,
    /// key bonus.
    ///
    /// This is a <b>display mirror</b> of <c>functions/src/lib/economy.ts</c>,
    /// not a second source of truth. The wallet is server-authoritative (ADR
    /// 0016, trust boundary 1): Cloud Functions decide what actually moves, and
    /// nothing here is ever written to Firestore. It exists so the lobby can
    /// answer "what does this table pay?" without a round trip, and so the
    /// answer is computed once rather than being retyped as a string on every
    /// screen that shows it.
    ///
    /// The constants are duplicated across the language boundary by necessity —
    /// <see cref="StakesTests"/> asserts them so a change on the server side
    /// fails a test here rather than quietly showing players the wrong pot.
    /// </summary>
    public static class Stakes
    {
        /// <summary>Flat coins each player stakes to enter a match.</summary>
        public const int EntryStake = 1000;

        /// <summary>Minted bonus added to the winner's payout per key scored.</summary>
        public const int KeyBonus = 2000;

        /// <summary>Fewest seats a table can be dealt for.</summary>
        public const int MinSeats = 2;

        /// <summary>Most seats a table can hold.</summary>
        public const int MaxSeats = 4;

        /// <summary>
        /// The pot for a table: every seated player stakes <see cref="EntryStake"/>.
        /// </summary>
        /// <param name="seats">Seats staking into this match, 2..4.</param>
        /// <returns>The total pot in coins.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Seats outside 2..4.</exception>
        public static int PotFor(int seats)
        {
            if (seats < MinSeats || seats > MaxSeats)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seats), seats, $"seats must be in {MinSeats}..{MaxSeats}.");
            }

            return EntryStake * seats;
        }

        /// <summary>
        /// What one winner walks away with, before any key bonus. Cut-Throat has
        /// a single winner taking the whole pot; Partner splits it between the
        /// two partners. Any indivisible remainder goes to the first winner, so
        /// the split never mints or loses a coin — same rule as the server's
        /// <c>splitPayout</c>.
        /// </summary>
        /// <param name="pot">The staked pot to distribute.</param>
        /// <param name="winners">How many players share the win (1 solo, 2 partners).</param>
        /// <returns>Coins for each winner, before key bonuses.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Fewer than one winner.</exception>
        public static int ShareOf(int pot, int winners)
        {
            if (winners < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(winners), winners, "winners must be at least 1.");
            }

            return pot / winners;
        }
    }
}
