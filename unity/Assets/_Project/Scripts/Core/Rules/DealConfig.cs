#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Configuration for the initial deal of a single round: which tile set to use,
    /// how many tiles each player receives, and the maximum pip value (used by the
    /// starting-player rule when scanning for the highest double).
    /// </summary>
    public sealed class DealConfig
    {
        public IReadOnlyList<Tile> TileSet { get; }
        public int TilesPerHand { get; }
        public byte MaxPip { get; }

        public DealConfig(IReadOnlyList<Tile> tileSet, int tilesPerHand, byte maxPip)
        {
            if (tileSet == null)
            {
                throw new ArgumentNullException(nameof(tileSet));
            }

            if (tileSet.Count == 0)
            {
                throw new ArgumentException(
                    "Tile set must contain at least one tile.",
                    nameof(tileSet));
            }

            if (tilesPerHand <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tilesPerHand),
                    "Tiles per hand must be positive.");
            }

            TileSet = tileSet;
            TilesPerHand = tilesPerHand;
            MaxPip = maxPip;
        }

        /// <summary>
        /// Standard double-six Cut-Throat configuration for the given player count.
        /// Encodes the canonical Jamaican deal counts: 2 players get 14 tiles each
        /// from the full 28-tile set (no sleeping tiles); 3 players get 9 each from a
        /// 27-tile set with <c>[0|0]</c> removed; 4 players get 7 each from the full
        /// set. Cut-Throat has no boneyard, so for 4 players every tile is dealt.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="playerCount"/> is not 2, 3, or 4.
        /// </exception>
        public static DealConfig CutThroatDoubleSix(int playerCount)
        {
            switch (playerCount)
            {
                case 2:
                    return new DealConfig(
                        tileSet: Pose.Core.TileSet.DoubleSix,
                        tilesPerHand: 14,
                        maxPip: 6);
                case 3:
                    return new DealConfig(
                        tileSet: DoubleSixWithoutDoubleZero,
                        tilesPerHand: 9,
                        maxPip: 6);
                case 4:
                    return new DealConfig(
                        tileSet: Pose.Core.TileSet.DoubleSix,
                        tilesPerHand: 7,
                        maxPip: 6);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(playerCount),
                        playerCount,
                        "Cut-Throat requires 2, 3, or 4 players.");
            }
        }

        // The 3-player Jamaican Cut-Throat deck: standard double-six minus [0|0], so
        // the 27 tiles divide evenly into three 9-tile hands with nothing left over.
        // Built lazily and cached because TileSet.DoubleSix is itself lazy.
        private static IReadOnlyList<Tile>? _doubleSixWithoutDoubleZero;
        private static IReadOnlyList<Tile> DoubleSixWithoutDoubleZero
        {
            get
            {
                if (_doubleSixWithoutDoubleZero != null)
                {
                    return _doubleSixWithoutDoubleZero;
                }

                IReadOnlyList<Tile> full = Pose.Core.TileSet.DoubleSix;
                List<Tile> filtered = new(full.Count - 1);
                Tile doubleZero = new(0, 0);
                for (int i = 0; i < full.Count; i++)
                {
                    if (full[i] != doubleZero)
                    {
                        filtered.Add(full[i]);
                    }
                }
                _doubleSixWithoutDoubleZero = filtered;
                return _doubleSixWithoutDoubleZero;
            }
        }
    }
}
