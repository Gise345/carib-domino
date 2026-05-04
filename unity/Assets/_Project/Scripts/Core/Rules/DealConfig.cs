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
        /// Standard double-six Block configuration: 28 tiles, 7 dealt per player.
        /// For 2 players, 14 tiles sleep face-down (Block has no boneyard draws).
        /// For 4 players, every tile is dealt.
        /// </summary>
        public static DealConfig BlockDoubleSix { get; } = new(
            tileSet: Pose.Core.TileSet.DoubleSix,
            tilesPerHand: 7,
            maxPip: 6);
    }
}
