#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// Pure, Unity-free geometry for rendering the played chain as a vertical
    /// snake. The opening tile anchors at the vertical center of the chain area;
    /// right-end plays walk downward and left-end plays walk upward, each fanning
    /// to opposite sides when a column overflows. At each bend a landscape
    /// "bridge" tile turns the corner, forming a horizontal three-tile run
    /// (last column tile → bridge → next column tile).
    ///
    /// Extracted from the renderer specifically so the bend geometry is testable
    /// (see Pose.Core.Tests.ChainLayoutTests). Two invariants a correct layout
    /// must hold, and which had regressed repeatedly while this lived in a
    /// MonoBehaviour:
    /// <list type="number">
    ///   <item>consecutive tiles touch — the incoming column meets the bridge
    ///         whether the outgoing column ended on a portrait tile or a
    ///         landscape double;</item>
    ///   <item>no two tiles overlap.</item>
    /// </list>
    /// Both cases hinge on the fact that a portrait tile is
    /// <see cref="ShortDim"/> wide while a landscape double is
    /// <see cref="LongDim"/> wide, so bend spacing must be derived from the
    /// actual tile at the corner, never a fixed constant.
    /// </summary>
    public static class ChainLayout
    {
        /// <summary>Short side of a tile (its width when portrait).</summary>
        public const float ShortDim = 60f;

        /// <summary>Long side of a tile (its height when portrait).</summary>
        public const float LongDim = 120f;

        /// <summary>Tunable geometry, defaulted to the renderer's values.</summary>
        public readonly struct Config
        {
            public readonly float TileSpacing;
            public readonly float VirtualHeight;
            public readonly float DropZoneHalfHeight;

            public Config(float tileSpacing, float virtualHeight, float dropZoneHalfHeight)
            {
                TileSpacing = tileSpacing;
                VirtualHeight = virtualHeight;
                DropZoneHalfHeight = dropZoneHalfHeight;
            }

            public static Config Default => new(tileSpacing: 2f, virtualHeight: 1700f, dropZoneHalfHeight: 70f);
        }

        private struct WalkState
        {
            public int Col;
            public float ColX;
            public float LastTileCenterY;
            public bool LastTileLandscape;
            public float NextEdgeY;
            public bool GoingDown;
            public int BendDirection;
            public bool PortraitPipsFlipped;
        }

        /// <summary>
        /// Computes slot positions for every tile in <paramref name="chain"/>.
        /// <paramref name="openingIdx"/> is the index of the opening tile (the
        /// one anchored at center). The chain must be non-empty.
        /// </summary>
        public static ChainLayoutResult Compute(Chain chain, int openingIdx, Config config)
        {
            ChainSlot[] slots = new ChainSlot[chain.Count];

            float innerH = config.VirtualHeight;
            float centerY = innerH / 2f;

            PlacedTile opening = chain.Tiles[openingIdx];
            bool openingLandscape = opening.Tile.IsDouble;
            float openingHalfH = openingLandscape ? ShortDim / 2f : LongDim / 2f;

            slots[openingIdx] = new ChainSlot(
                0f, centerY, openingLandscape, opening.LeftPip, opening.RightPip);

            // Right-end plays: walk downward, bending right.
            WalkState down = new()
            {
                Col = 0,
                ColX = 0f,
                LastTileCenterY = centerY,
                LastTileLandscape = openingLandscape,
                NextEdgeY = centerY + openingHalfH + config.TileSpacing,
                GoingDown = true,
                BendDirection = +1,
                PortraitPipsFlipped = false,
            };
            for (int i = openingIdx + 1; i < chain.Count; i++)
            {
                WalkPlace(chain, slots, i, ref down, config);
            }

            // Left-end plays: walk upward, bending left.
            WalkState up = new()
            {
                Col = 0,
                ColX = 0f,
                LastTileCenterY = centerY,
                LastTileLandscape = openingLandscape,
                NextEdgeY = centerY - openingHalfH - config.TileSpacing,
                GoingDown = false,
                BendDirection = -1,
                PortraitPipsFlipped = false,
            };
            for (int i = openingIdx - 1; i >= 0; i--)
            {
                WalkPlace(chain, slots, i, ref up, config);
            }

            float rightZoneY = down.GoingDown
                ? down.NextEdgeY + config.DropZoneHalfHeight
                : down.NextEdgeY - config.DropZoneHalfHeight;
            float leftZoneY = up.GoingDown
                ? up.NextEdgeY + config.DropZoneHalfHeight
                : up.NextEdgeY - config.DropZoneHalfHeight;

            return new ChainLayoutResult(
                slots,
                leftZoneX: up.ColX,
                leftZoneY: leftZoneY,
                rightZoneX: down.ColX,
                rightZoneY: rightZoneY);
        }

        private static void WalkPlace(
            Chain chain,
            ChainSlot[] slots,
            int i,
            ref WalkState state,
            Config config)
        {
            PlacedTile pt = chain.Tiles[i];
            bool isDouble = pt.Tile.IsDouble;
            // Height along the column axis: a double lies landscape (ShortDim
            // tall), a regular tile stands portrait (LongDim tall).
            float tentativeH = isDouble ? ShortDim : LongDim;

            bool willBend = state.GoingDown
                ? state.NextEdgeY + tentativeH > config.VirtualHeight
                : state.NextEdgeY - tentativeH < 0f;

            if (willBend)
            {
                PlaceBridge(chain, slots, i, ref state, config);
            }
            else
            {
                PlaceRegular(slots, i, pt, isDouble, tentativeH, ref state, config);
            }
        }

        /// <summary>
        /// Turns the corner. The bridge lies landscape at the SAME center-Y as
        /// the outgoing column's last tile — no edge shift, so it stays aligned
        /// whether that last tile was portrait or a double. Column spacing on
        /// both sides of the bridge is derived from the real half-widths of the
        /// outgoing last tile and the incoming first tile, so both neighbours
        /// touch it exactly.
        /// </summary>
        private static void PlaceBridge(
            Chain chain,
            ChainSlot[] slots,
            int i,
            ref WalkState state,
            Config config)
        {
            int dir = state.BendDirection;
            float bendY = state.LastTileCenterY;

            float lastHalfW = state.LastTileLandscape ? LongDim / 2f : ShortDim / 2f;
            float bridgeHalfW = LongDim / 2f;

            float bridgeCenterX = state.ColX + dir * (lastHalfW + config.TileSpacing + bridgeHalfW);

            // Peek the first tile of the next column to size the gap on the far
            // side of the bridge and to center that tile on the bridge's Y.
            bool nextIsDouble = i + 1 < chain.Count && chain.Tiles[i + 1].Tile.IsDouble;
            float nextHalfW = nextIsDouble ? LongDim / 2f : ShortDim / 2f;
            float nextH = nextIsDouble ? ShortDim : LongDim;

            float newColX = bridgeCenterX + dir * (bridgeHalfW + config.TileSpacing + nextHalfW);

            PlacedTile bridge = chain.Tiles[i];
            slots[i] = new ChainSlot(
                bridgeCenterX, bendY, landscape: true, bridge.LeftPip, bridge.RightPip);

            state.Col++;
            state.ColX = newColX;
            state.PortraitPipsFlipped = !state.PortraitPipsFlipped;
            state.LastTileCenterY = bendY;
            state.LastTileLandscape = false;

            bool goingDown = !state.GoingDown;
            state.GoingDown = goingDown;
            // Seat the new column's first tile centered on the bridge's Y, then
            // continue in the new vertical direction.
            state.NextEdgeY = goingDown ? bendY - nextH / 2f : bendY + nextH / 2f;
        }

        private static void PlaceRegular(
            ChainSlot[] slots,
            int i,
            PlacedTile pt,
            bool isDouble,
            float tileH,
            ref WalkState state,
            Config config)
        {
            float centerY = state.GoingDown
                ? state.NextEdgeY + tileH / 2f
                : state.NextEdgeY - tileH / 2f;

            byte firstPip, secondPip;
            if (isDouble)
            {
                // Landscape: halves read left→right; both pips equal anyway.
                firstPip = pt.LeftPip;
                secondPip = pt.RightPip;
            }
            else if (state.PortraitPipsFlipped)
            {
                // After a bend, predecessor and successor swap visual sides.
                firstPip = pt.RightPip;
                secondPip = pt.LeftPip;
            }
            else
            {
                firstPip = pt.LeftPip;
                secondPip = pt.RightPip;
            }

            slots[i] = new ChainSlot(state.ColX, centerY, isDouble, firstPip, secondPip);

            state.LastTileCenterY = centerY;
            state.LastTileLandscape = isDouble;
            if (state.GoingDown)
            {
                state.NextEdgeY += tileH + config.TileSpacing;
            }
            else
            {
                state.NextEdgeY -= tileH + config.TileSpacing;
            }
        }
    }
}
