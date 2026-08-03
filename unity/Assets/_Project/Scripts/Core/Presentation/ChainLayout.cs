#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// Pure, Unity-free geometry for rendering the played chain as a vertical
    /// snake. The opening tile anchors at the vertical center of the chain area;
    /// right-end plays walk downward and left-end plays walk upward, each fanning
    /// to opposite sides when a column overflows. At each bend a landscape
    /// "bridge" tile turns the corner.
    ///
    /// Orientation rules:
    /// <list type="bullet">
    ///   <item>Regular tiles stand portrait (along the column).</item>
    ///   <item>Doubles lie landscape (crosswise) — the traditional table look —
    ///         EXCEPT the first tile of a column after a bend, which stands
    ///         portrait so the new column reads as starting vertically.</item>
    ///   <item>The bend bridge lies landscape, with its TOP edge aligned to the
    ///         outgoing column tile's top edge (not centered on it).</item>
    /// </list>
    ///
    /// Extracted from the renderer so the bend geometry is testable (see
    /// Pose.Core.Tests.ChainLayoutTests): consecutive tiles touch, no two tiles
    /// overlap, and each bridge's top aligns with its outgoing tile's top.
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
            // True for the first regular tile of a column just after a bend — a
            // double there stands portrait rather than lying crosswise.
            public bool FirstTileOfColumn;
        }

        private static float HalfWidth(bool landscape) => landscape ? LongDim / 2f : ShortDim / 2f;

        private static float HalfHeight(bool landscape) => landscape ? ShortDim / 2f : LongDim / 2f;

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

            // The opening is always a double and — being the first tile of the
            // FIRST column, not a post-bend column — lies landscape (crosswise).
            PlacedTile opening = chain.Tiles[openingIdx];
            bool openingLandscape = opening.Tile.IsDouble;
            float openingHalfH = HalfHeight(openingLandscape);

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
                FirstTileOfColumn = false,
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
                FirstTileOfColumn = false,
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
            // A double lies landscape (crosswise) unless it is the first tile of
            // this column after a bend, in which case it stands portrait.
            bool landscape = pt.Tile.IsDouble && !state.FirstTileOfColumn;
            float tentativeH = landscape ? ShortDim : LongDim;

            bool willBend = state.GoingDown
                ? state.NextEdgeY + tentativeH > config.VirtualHeight
                : state.NextEdgeY - tentativeH < 0f;

            if (willBend)
            {
                PlaceBridge(chain, slots, i, ref state, config);
            }
            else
            {
                PlaceRegular(slots, i, pt, landscape, tentativeH, ref state, config);
            }
        }

        /// <summary>
        /// Turns the corner. The bridge lies landscape with its TOP edge aligned
        /// to the outgoing column tile's top edge. Column spacing derives from the
        /// outgoing tile's actual half-width (a crosswise double is wider than a
        /// portrait tile), so both neighbours touch the bridge exactly.
        /// </summary>
        private static void PlaceBridge(
            Chain chain,
            ChainSlot[] slots,
            int i,
            ref WalkState state,
            Config config)
        {
            int dir = state.BendDirection;

            float lastHalfW = HalfWidth(state.LastTileLandscape);
            float lastHalfH = HalfHeight(state.LastTileLandscape);
            float bridgeHalfW = LongDim / 2f;
            float bridgeHalfH = ShortDim / 2f;

            // Top-align: bridge top edge == outgoing tile top edge.
            float topEdge = state.LastTileCenterY - lastHalfH;
            float bridgeCenterY = topEdge + bridgeHalfH;

            float bridgeCenterX = state.ColX + dir * (lastHalfW + config.TileSpacing + bridgeHalfW);
            // The new column's first tile is always portrait (a first-of-column
            // double stands portrait), so its half-width is ShortDim / 2.
            float newColX = bridgeCenterX + dir * (bridgeHalfW + config.TileSpacing + ShortDim / 2f);

            PlacedTile bridge = chain.Tiles[i];
            slots[i] = new ChainSlot(
                bridgeCenterX, bridgeCenterY, landscape: true, bridge.LeftPip, bridge.RightPip);

            // LastTileCenterY stays at the outgoing tile's center — the new
            // column's first tile re-anchors from it below.
            float bendCenterY = state.LastTileCenterY;

            state.Col++;
            state.ColX = newColX;
            state.PortraitPipsFlipped = !state.PortraitPipsFlipped;
            state.LastTileLandscape = false;
            state.FirstTileOfColumn = true;

            bool goingDown = !state.GoingDown;
            state.GoingDown = goingDown;
            // Seat the new column's first tile (portrait, LongDim tall) centered on
            // the outgoing tile's center-Y, then continue in the new direction.
            state.NextEdgeY = goingDown ? bendCenterY - LongDim / 2f : bendCenterY + LongDim / 2f;
        }

        private static void PlaceRegular(
            ChainSlot[] slots,
            int i,
            PlacedTile pt,
            bool landscape,
            float tileH,
            ref WalkState state,
            Config config)
        {
            float centerY = state.GoingDown
                ? state.NextEdgeY + tileH / 2f
                : state.NextEdgeY - tileH / 2f;

            byte firstPip, secondPip;
            if (landscape)
            {
                // Landscape halves read left→right; a double's pips are equal anyway.
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

            slots[i] = new ChainSlot(state.ColX, centerY, landscape, firstPip, secondPip);

            state.LastTileCenterY = centerY;
            state.LastTileLandscape = landscape;
            state.FirstTileOfColumn = false;
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
