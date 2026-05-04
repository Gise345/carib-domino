#nullable enable
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Static factories for the standard domino tile sets used by various variants.
    /// Each set is generated once and cached; callers receive a stable, read-only
    /// reference suitable for use as a deterministic shuffle source.
    /// </summary>
    public static class TileSet
    {
        /// <summary>
        /// Standard double-six set: 28 tiles, all pip combinations from
        /// <c>[0,0]</c> to <c>[6,6]</c>. Used by Block, Draw, All Fives, Jamaican,
        /// Trinidadian, and Puerto Rican variants.
        /// </summary>
        public static IReadOnlyList<Tile> DoubleSix { get; } = Generate(6);

        /// <summary>
        /// Standard double-nine set: 55 tiles. Used by Cuban and most Mexican Train
        /// variants.
        /// </summary>
        public static IReadOnlyList<Tile> DoubleNine { get; } = Generate(9);

        /// <summary>
        /// Standard double-twelve set: 91 tiles. Used by some Mexican Train variants.
        /// </summary>
        public static IReadOnlyList<Tile> DoubleTwelve { get; } = Generate(12);

        private static IReadOnlyList<Tile> Generate(byte maxPip)
        {
            List<Tile> tiles = new();
            for (byte a = 0; a <= maxPip; a++)
            {
                for (byte b = a; b <= maxPip; b++)
                {
                    tiles.Add(new Tile(a, b));
                }
            }
            return tiles;
        }
    }
}
