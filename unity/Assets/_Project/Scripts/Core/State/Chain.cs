#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// The current chain of placed tiles on the table. Immutable: <see cref="Place"/>
    /// returns a new chain. Internally tile order is left-to-right; the open pip at
    /// each end is tracked so the rule engine never has to walk the chain to validate
    /// a placement.
    /// </summary>
    public sealed class Chain
    {
        private readonly IReadOnlyList<PlacedTile> _tiles;

        /// <summary>The empty chain. Use this as the starting state for a new round.</summary>
        public static Chain Empty { get; } = new(Array.Empty<PlacedTile>());

        private Chain(IReadOnlyList<PlacedTile> tiles)
        {
            _tiles = tiles;
        }

        public int Count => _tiles.Count;
        public bool IsEmpty => _tiles.Count == 0;
        public IReadOnlyList<PlacedTile> Tiles => _tiles;

        /// <summary>
        /// The currently open pip value at the left end of the chain.
        /// Throws when the chain is empty.
        /// </summary>
        public byte LeftEnd
        {
            get
            {
                if (IsEmpty)
                {
                    throw new InvalidOperationException(
                        "Empty chain has no left end.");
                }

                return _tiles[0].LeftPip;
            }
        }

        /// <summary>
        /// The currently open pip value at the right end of the chain.
        /// Throws when the chain is empty.
        /// </summary>
        public byte RightEnd
        {
            get
            {
                if (IsEmpty)
                {
                    throw new InvalidOperationException(
                        "Empty chain has no right end.");
                }

                return _tiles[_tiles.Count - 1].RightPip;
            }
        }

        /// <summary>
        /// Places <paramref name="tile"/> at the requested end and returns a new chain.
        /// The tile is rotated automatically to match the existing end. For an empty
        /// chain both ends are open; the tile is laid as-is (<see cref="Tile.A"/>
        /// becomes the left pip, <see cref="Tile.B"/> the right) regardless of which
        /// end is requested.
        /// </summary>
        public Chain Place(Tile tile, ChainEnd end)
        {
            if (IsEmpty)
            {
                PlacedTile first = new(tile, leftPip: tile.A, rightPip: tile.B);
                return new Chain(new[] { first });
            }

            if (end == ChainEnd.Left)
            {
                if (!tile.Matches(LeftEnd))
                {
                    throw new ArgumentException(
                        $"Tile {tile} does not match LEFT end (pip {LeftEnd}).");
                }

                byte newOuter = tile.GetOther(LeftEnd);
                List<PlacedTile> next = new(_tiles.Count + 1)
                {
                    new PlacedTile(tile, leftPip: newOuter, rightPip: LeftEnd),
                };
                next.AddRange(_tiles);
                return new Chain(next);
            }
            else
            {
                if (!tile.Matches(RightEnd))
                {
                    throw new ArgumentException(
                        $"Tile {tile} does not match RIGHT end (pip {RightEnd}).");
                }

                byte newOuter = tile.GetOther(RightEnd);
                List<PlacedTile> next = new(_tiles);
                next.Add(new PlacedTile(tile, leftPip: RightEnd, rightPip: newOuter));
                return new Chain(next);
            }
        }
    }
}
