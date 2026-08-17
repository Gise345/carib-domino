#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Where one tile lies on the table during the opening shuffle.
    /// Coordinates are relative to the centre of the scatter field.
    /// </summary>
    public readonly struct ScatterPlacement
    {
        /// <param name="x">Offset from field centre, positive to the right.</param>
        /// <param name="y">Offset from field centre, positive upwards.</param>
        /// <param name="angleDegrees">Resting tilt of the tile.</param>
        public ScatterPlacement(float x, float y, float angleDegrees)
        {
            X = x;
            Y = y;
            AngleDegrees = angleDegrees;
        }

        /// <summary>Offset from field centre, positive to the right.</summary>
        public float X { get; }

        /// <summary>Offset from field centre, positive upwards.</summary>
        public float Y { get; }

        /// <summary>Resting tilt of the tile.</summary>
        public float AngleDegrees { get; }
    }

    /// <summary>
    /// Lays a set of tiles out the way a hand-shuffled set actually lies: no
    /// rows, no columns, every tile at its own angle, neighbours overlapping
    /// where they fall.
    ///
    /// A grid still exists, but only as the anti-heap device — each tile holds
    /// exactly one cell, so the set can never bulk up in one corner of the
    /// table. Everything visible is the jitter inside the cell and the tilt on
    /// top of it. Re-asking with the next <c>cycle</c> re-deals the cells, which
    /// is what makes tiles travel across each other rather than wobble in place.
    ///
    /// This is cosmetic only. The real deal comes from the server seed by way of
    /// <see cref="IRandomSource"/>; nothing here may influence or reveal it, so
    /// placements are derived from a plain hash of (tile, cycle) instead. That
    /// also makes them stable — asking twice in a frame gives the same answer,
    /// so the layout never jumps.
    /// </summary>
    public sealed class ShuffleScatter
    {
        private readonly int _tileCount;
        private readonly int _columns;
        private readonly int _rows;
        private readonly float _fieldWidth;
        private readonly float _fieldHeight;
        private readonly float _angleSpread;
        private readonly float _jitter;

        // One cycle's cell assignment, kept because the renderer asks for every
        // tile of a cycle in the same frame. It covers every cell, not just the
        // occupied ones, so the gaps in a part-filled last row move around with
        // the tiles instead of always sitting in the same corner.
        private readonly int[] _cells;
        private int _cachedCycle = -1;

        /// <param name="tileCount">How many tiles are on the table.</param>
        /// <param name="columns">Cells across. Rows follow from the count.</param>
        /// <param name="fieldWidth">Width of the area tiles may occupy.</param>
        /// <param name="fieldHeight">Height of the area tiles may occupy.</param>
        /// <param name="angleSpreadDegrees">Maximum tilt either way from upright.</param>
        /// <param name="jitterFraction">
        /// How far a tile may sit from its cell centre, as a fraction of the
        /// cell. Must stay under a half so a tile can never leave its own cell,
        /// which is what bounds the overlap.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when any argument would produce a degenerate field.
        /// </exception>
        public ShuffleScatter(
            int tileCount,
            int columns,
            float fieldWidth,
            float fieldHeight,
            float angleSpreadDegrees,
            float jitterFraction)
        {
            if (tileCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileCount), "There must be at least one tile to place.");
            }

            if (columns <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columns), "The field needs at least one column.");
            }

            if (fieldWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fieldWidth), "The field must have width.");
            }

            if (fieldHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fieldHeight), "The field must have height.");
            }

            if (angleSpreadDegrees < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(angleSpreadDegrees), "Tilt cannot be negative.");
            }

            if (jitterFraction < 0f || jitterFraction >= 0.5f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(jitterFraction),
                    "Jitter must keep every tile inside its own cell (0 <= j < 0.5).");
            }

            _tileCount = tileCount;
            _columns = columns;
            _rows = ((tileCount - 1) / columns) + 1;
            _fieldWidth = fieldWidth;
            _fieldHeight = fieldHeight;
            _angleSpread = angleSpreadDegrees;
            _jitter = jitterFraction;
            _cells = new int[_rows * columns];
        }

        /// <summary>Cells across the field.</summary>
        public int Columns => _columns;

        /// <summary>Cells down the field.</summary>
        public int Rows => _rows;

        /// <summary>
        /// Cells in the field. A part-filled last row leaves more cells than
        /// tiles, and those gaps move with every cycle.
        /// </summary>
        public int CellCount => _cells.Length;

        /// <summary>
        /// How far a whole row slides sideways, as a fraction of a cell. This is
        /// what stops columns forming: every row sits at its own offset, so no
        /// two tiles above one another share an edge. Rows are left alone —
        /// staggering both axes would let a tile from another row and column
        /// drift on top of one, and the separation floor would stop meaning
        /// anything. Callers size the field with it: a field needs
        /// <c>columns + 2 * StaggerFraction</c> cells of width to hold the slack.
        /// </summary>
        public const float StaggerFraction = 0.5f;

        /// <summary>
        /// Smallest tilt any tile takes, as a fraction of the spread. Tilts
        /// drawn evenly across the range leave a handful of tiles near upright,
        /// and upright tiles are exactly what makes a shuffle look laid out.
        /// </summary>
        private const float MinimumTiltFraction = 0.35f;

        /// <summary>Width of one cell.</summary>
        public float CellWidth => _fieldWidth / (_columns + (2f * StaggerFraction));

        /// <summary>Height of one cell.</summary>
        public float CellHeight => _fieldHeight / _rows;

        /// <summary>
        /// Closest two tile centres can ever come. Tiles are wider than this, so
        /// they do overlap — but the floor is what stops them heaping.
        /// </summary>
        public float MinimumSeparation =>
            Math.Min(CellWidth, CellHeight) * (1f - (2f * _jitter));

        /// <summary>
        /// Which cell a tile holds in the given cycle. Every cycle is a fresh
        /// permutation, so this is also a usable draw order — tiles that pass
        /// over each other keep swapping which one is on top.
        /// </summary>
        /// <param name="tileIndex">Tile to look up.</param>
        /// <param name="cycle">Shuffle cycle, counting from zero.</param>
        /// <returns>The cell index, row-major from the top-left.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the tile is outside the set or the cycle is negative.
        /// </exception>
        public int CellOf(int tileIndex, int cycle)
        {
            if (tileIndex < 0 || tileIndex >= _tileCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileIndex), "No such tile in this set.");
            }

            EnsureCycle(cycle);
            return _cells[tileIndex];
        }

        /// <summary>
        /// Where a tile lies, and at what angle, in the given cycle.
        /// </summary>
        /// <param name="tileIndex">Tile to place.</param>
        /// <param name="cycle">Shuffle cycle, counting from zero.</param>
        /// <returns>The placement, relative to the centre of the field.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when the tile is outside the set or the cycle is negative.
        /// </exception>
        public ScatterPlacement Placement(int tileIndex, int cycle)
        {
            int cell = CellOf(tileIndex, cycle);
            int col = cell % _columns;
            int row = cell / _columns;

            float cellW = CellWidth;
            float cellH = CellHeight;

            // The row's own sideways offset, so columns never line up.
            float stagger =
                Signed(Hash((uint)row + 1u, (uint)cycle ^ StaggerSalt))
                * StaggerFraction * cellW;

            float centreX = ((col + 0.5f) * cellW) - (_columns * cellW * 0.5f) + stagger;
            float centreY = (_rows * cellH * 0.5f) - ((row + 0.5f) * cellH);

            uint h = Hash((uint)tileIndex, (uint)cycle);
            float x = centreX + (Signed(h) * _jitter * cellW);
            float y = centreY + (Signed(Hash(h, JitterSalt)) * _jitter * cellH);

            // Tilt away from upright, either way, never flat.
            float tilt = Signed(Hash(h, AngleSalt));
            float magnitude = MinimumTiltFraction
                + (Math.Abs(tilt) * (1f - MinimumTiltFraction));
            float angle = (tilt < 0f ? -magnitude : magnitude) * _angleSpread;

            return new ScatterPlacement(x, y, angle);
        }

        /// <summary>
        /// Re-deals the cells for a cycle. Cosmetic randomness only, so the
        /// modulo here is fine — the unbiased path is <see cref="IRandomSource"/>
        /// and is reserved for anything that touches a hand.
        /// </summary>
        private void EnsureCycle(int cycle)
        {
            if (cycle < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cycle), "Cycles count from zero.");
            }

            if (_cachedCycle == cycle)
            {
                return;
            }

            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = i;
            }

            for (int i = _cells.Length - 1; i > 0; i--)
            {
                int j = (int)(Hash((uint)cycle, (uint)i) % (uint)(i + 1));
                (_cells[i], _cells[j]) = (_cells[j], _cells[i]);
            }

            _cachedCycle = cycle;
        }

        private const uint JitterSalt = 0x51ED2701u;
        private const uint AngleSalt = 0x1B873593u;
        private const uint StaggerSalt = 0xCC9E2D51u;

        private static uint Hash(uint a, uint b)
        {
            unchecked
            {
                uint h = (a * 0x9E3779B1u) ^ (b + 0x85EBCA6Bu + (a << 6) + (a >> 2));
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>Maps a hash onto -1..1.</summary>
        private static float Signed(uint h) =>
            (((h >> 8) * (1f / 16777216f)) * 2f) - 1f;
    }
}
