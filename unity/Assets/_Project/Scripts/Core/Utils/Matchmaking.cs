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

        /// <summary>
        /// The matchmaking property set for a random Cut-Throat table of the
        /// given size. Two players calling this with the same size produce
        /// identical dictionaries, so Photon matches them into one table.
        /// </summary>
        /// <param name="size">Table size, 2–4.</param>
        /// <returns>Key→value pairs to publish as session properties.</returns>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="size"/> is not 2–4.</exception>
        public static IReadOnlyDictionary<string, string> CutThroatProperties(int size)
        {
            if (size < 2 || size > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size), size, "Cut-Throat online table size must be 2, 3, or 4.");
            }

            return new Dictionary<string, string>
            {
                [PropMode] = ModeCutThroat,
                [PropSize] = size.ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
