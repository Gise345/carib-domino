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
    /// Every in-run tile stands portrait — doubles included, so a double reads
    /// like a regular tile rather than lying crosswise. The only landscape tile
    /// is the bend bridge, which must lie across to turn the corner.
    ///
    /// Extracted from the renderer specifically so the bend geometry is testable
    /// (see Pose.Core.Tests.ChainLayoutTests). Invariants a correct layout must
    /// hold, and which had regressed repeatedly while this lived in a
    /// MonoBehaviour: consecutive tiles touch, no two tiles overlap, and each
    /// bridge aligns with the outgoing column's last tile.
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

            // The opening stands portrait like any in-run tile (even though it
            // is always a double), so half its height is LongDim / 2.
            PlacedTile opening = chain.Tiles[openingIdx];
            float openingHalfH = LongDim / 2f;

            slots[openingIdx] = new ChainSlot(
                0f, centerY, landscape: false, opening.LeftPip, opening.RightPip);

            // Right-end plays: walk downward, bending right.
            WalkState down = new()
            {
                Col = 0,
                ColX = 0f,
                LastTileCenterY = centerY,
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
            // Every in-run tile stands portrait (LongDim tall), doubles included.
            const float tentativeH = LongDim;

            bool willBend = state.GoingDown
                ? state.NextEdgeY + tentativeH > config.VirtualHeight
                : state.NextEdgeY - tentativeH < 0f;

            if (willBend)
            {
                PlaceBridge(chain, slots, i, ref state, config);
            }
            else
            {
                PlaceRegular(slots, i, pt, tentativeH, ref state, config);
            }
        }

        /// <summary>
        /// Turns the corner. The bridge lies landscape at the SAME center-Y as
        /// the outgoing column's last tile — no edge shift, so it stays aligned.
        /// Since every in-run tile is portrait (half-width ShortDim / 2), the
        /// gap on both sides of the bridge is identical and the bend has a
        /// single case; both neighbours touch it exactly.
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

            float halfCol = ShortDim / 2f;
            float bridgeHalfW = LongDim / 2f;

            float bridgeCenterX = state.ColX + dir * (halfCol + config.TileSpacing + bridgeHalfW);
            float newColX = bridgeCenterX + dir * (bridgeHalfW + config.TileSpacing + halfCol);

            PlacedTile bridge = chain.Tiles[i];
            slots[i] = new ChainSlot(
                bridgeCenterX, bendY, landscape: true, bridge.LeftPip, bridge.RightPip);

            state.Col++;
            state.ColX = newColX;
            state.PortraitPipsFlipped = !state.PortraitPipsFlipped;
            state.LastTileCenterY = bendY;

            bool goingDown = !state.GoingDown;
            state.GoingDown = goingDown;
            // Seat the new column's first tile (portrait, LongDim tall) centered
            // on the bridge's Y, then continue in the new vertical direction.
            state.NextEdgeY = goingDown ? bendY - LongDim / 2f : bendY + LongDim / 2f;
        }

        private static void PlaceRegular(
            ChainSlot[] slots,
            int i,
            PlacedTile pt,
            float tileH,
            ref WalkState state,
            Config config)
        {
            float centerY = state.GoingDown
                ? state.NextEdgeY + tileH / 2f
                : state.NextEdgeY - tileH / 2f;

            // Portrait halves read top→bottom. Before the first bend the chain
            // predecessor sits above (TOP = LeftPip); after each bend the
            // predecessor and successor swap sides, so the pips flip. Doubles
            // have equal pips, so the flip is a no-op for them.
            byte firstPip = state.PortraitPipsFlipped ? pt.RightPip : pt.LeftPip;
            byte secondPip = state.PortraitPipsFlipped ? pt.LeftPip : pt.RightPip;

            slots[i] = new ChainSlot(state.ColX, centerY, landscape: false, firstPip, secondPip);

            state.LastTileCenterY = centerY;
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
