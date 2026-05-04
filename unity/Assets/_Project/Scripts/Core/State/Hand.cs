#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// A single player's hand of tiles. Immutable: <see cref="Without"/> returns a new
    /// hand with the requested tile removed. Internal order is preserved (the order
    /// the tiles were dealt) so that the same seeded deal always produces the same
    /// observable hand state.
    /// </summary>
    public sealed class Hand : IReadOnlyCollection<Tile>
    {
        private readonly Tile[] _tiles;

        public Hand(IEnumerable<Tile> tiles)
        {
            if (tiles == null)
            {
                throw new ArgumentNullException(nameof(tiles));
            }

            _tiles = new List<Tile>(tiles).ToArray();
        }

        public static Hand Empty { get; } = new(Array.Empty<Tile>());

        public int Count => _tiles.Length;

        /// <summary>Sum of pips across every tile in the hand.</summary>
        public int PipTotal
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _tiles.Length; i++)
                {
                    total += _tiles[i].Pips;
                }
                return total;
            }
        }

        public bool Contains(Tile tile)
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == tile)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns a new hand with the first occurrence of <paramref name="tile"/>
        /// removed, preserving the order of the remaining tiles. Throws if the tile
        /// is not in the hand.
        /// </summary>
        public Hand Without(Tile tile)
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] == tile)
                {
                    Tile[] next = new Tile[_tiles.Length - 1];
                    Array.Copy(_tiles, 0, next, 0, i);
                    Array.Copy(_tiles, i + 1, next, i, _tiles.Length - i - 1);
                    return new Hand(next);
                }
            }

            throw new InvalidOperationException(
                $"Hand does not contain {tile}.");
        }

        public IEnumerator<Tile> GetEnumerator()
        {
            for (int i = 0; i < _tiles.Length; i++)
            {
                yield return _tiles[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
