#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pose.Core
{
    /// <summary>
    /// Photon session-matchmaking keys for random online play ("Cut Throat
    /// Online"). The player who creates a random session and every player who
    /// joins it must publish the SAME property set, or Photon won't group them
    /// into one table — so the keys and values live here, pure and unit-tested,
    /// and the Fusion wrapper (<c>PhotonBootstrap</c>) does nothing but convert
    /// them to <c>SessionProperty</c>. Values mirror the server's wire strings
    /// (see <c>MatchService</c> / <c>startMatch</c>).
    /// </summary>
    public static class Matchmaking
    {
        /// <summary>Property key carrying the ruleset (e.g. "cutthroat").</summary>
        public const string PropMode = "mode";

        /// <summary>Property key carrying the table size (2–4, as a string).</summary>
        public const string PropSize = "size";

        /// <summary>Wire value for Cut-Throat, matching <see cref="GameMode.CutThroat"/>'s "cutthroat".</summary>
        public const string ModeCutThroat = "cutthroat";

        /// <summary>Wire value for Jamaican Partner, matching <see cref="GameMode.Partner"/>'s "partner".</summary>
        public const string ModePartner = "partner";

        /// <summary>Jamaican Partner is always a 4-seat, 2-v-2 table.</summary>
        public const int PartnerSize = 4;

        /// <summary>Property key carrying the series format ("classic"/"quick").</summary>
        public const string PropFormat = "fmt";

        /// <summary>
        /// The matchmaking property set for a random online table. Two players
        /// calling this with the same mode and size produce identical
        /// dictionaries, so Photon groups them into one table — and different
        /// modes, sizes or formats never cross-match. Partner ignores
        /// <paramref name="size"/>: it is always <see cref="PartnerSize"/>
        /// (2 v 2), but it does honour <paramref name="format"/>.
        /// </summary>
        /// <param name="mode">The ruleset to matchmake for.</param>
        /// <param name="size">Table size, 2–4 (Cut-Throat only).</param>
        /// <returns>Key→value pairs to publish as session properties.</returns>
        /// <exception cref="ArgumentOutOfRangeException">If a Cut-Throat <paramref name="size"/> is not 2–4.</exception>
        public static IReadOnlyDictionary<string, string> Properties(GameMode mode, int size, MatchFormat format)
        {
            if (mode == GameMode.Partner)
            {
                // Partner splits by format too. Without this a Classic Partner
                // seeker and a Quick Partner seeker group into one table and
                // only the host's series length applies — the other player
                // silently gets a match they did not pick.
                return new Dictionary<string, string>
                {
                    [PropMode] = ModePartner,
                    [PropSize] = PartnerSize.ToString(CultureInfo.InvariantCulture),
                    [PropFormat] = MatchFormatRules.ToWire(format),
                };
            }

            if (size < 2 || size > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size), size, "Cut-Throat online table size must be 2, 3, or 4.");
            }

            return new Dictionary<string, string>
            {
                [PropMode] = ModeCutThroat,
                [PropSize] = size.ToString(CultureInfo.InvariantCulture),
                [PropFormat] = MatchFormatRules.ToWire(format),
            };
        }

        /// <summary>Convenience: <see cref="Properties"/> for a Cut-Throat table.</summary>
        public static IReadOnlyDictionary<string, string> CutThroatProperties(int size) =>
            Properties(GameMode.CutThroat, size, MatchFormat.ClassicSixLove);
    }
}
