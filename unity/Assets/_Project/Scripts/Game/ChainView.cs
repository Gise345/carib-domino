#nullable enable
using System.Collections.Generic;
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Renders the played chain as a vertical snake. The OPENING tile (the
    /// first one played in the round) anchors at the vertical center of the
    /// chain area and never moves as the round goes on. Tiles played on the
    /// chain's RIGHT end extend downward from the opening; tiles played on
    /// the LEFT end extend upward.
    ///
    /// When a column overflows, the bend is rendered with a forced-landscape
    /// "elbow" tile positioned at the SAME Y level as the last regular tile
    /// of the column. The next column's first regular tile is also at that
    /// same Y, in the adjacent column to the left — producing a clean,
    /// connected L-bridge across the bend (matches Giselle's M3.5 sketch).
    ///
    /// Doubles also render landscape (perpendicular to the chain direction),
    /// forming the cross visual a real table shows.
    ///
    /// Pip rendering uses <see cref="PlacedTile.LeftPip"/> /
    /// <see cref="PlacedTile.RightPip"/> rather than the canonical Tile.A/B
    /// order, so a [3|5] played on a 5-end shows the 5 facing the chain.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ChainView : MonoBehaviour
    {
        // ColumnWidth = LongDim accommodates a landscape double's full width.
        // ColumnSpacing = 60 leaves exactly LongDim of gap between adjacent
        // portrait tiles, which is what the leveled elbow tile needs to fit
        // horizontally between two columns without overlap.
        private const float ColumnWidth = TileView.LongDim;
        // Adjacent columns are spaced ShortDim + LongDim apart so the
        // bridge tile fits BETWEEN them as its own slot. The chain line
        // (col N's center, col N+1's center, and the bridge's center) all
        // sit at the same Y level, forming a "valid horizontal run" of
        // three tiles end-to-end at the bend. No tile overlaps another
        // and the bridge doesn't overhang either column.
        //
        //   col N right edge | bridge left edge ... bridge right edge | col N+1 left edge
        //         +30        |       -30        ...        -150        |         -150
        //
        // Translating: col N at X=0 → col N right=+30 → bridge spans -150
        // to -30 (center -90) → col N+1 right=-150 → col N+1 center=-180.
        // Spacing = 0 - (-180) = 180 = ShortDim + LongDim.
        private const float ColumnCenterSpacing = TileView.ShortDim + TileView.LongDim;
        private const float TileSpacing = 2f;
        private const float HeadRoom = 80f;
        // Larger than HeadRoom so a bridge placed just past the last tile
        // of a fully-loaded down-walking column still fits within the
        // chain area's visible bounds.
        private const float FootRoom = 80f;
        private const float DropZoneWidth = TileView.LongDim - 8f;
        private const float DropZoneHeight = 56f;
        // Logical chain area height (independent of actual screen resolution)
        // so both phones produce identical bend points for the same chain.
        // The container itself is still flex-sized in the parent VLG — this
        // only fixes the walker's bend math. 1700 px lets each column hold
        // ~13 portrait tiles before bending, which means a double-six round
        // typically ends in 2 columns and doesn't trigger early bends on
        // taller phones.
        private const float VirtualInnerHeight = 1700f;

        private const float LabelFontSize = 22f;
        private const float LabelHeight = 30f;

        private static readonly Color LabelColor = new(0.95f, 0.92f, 0.85f);

        public EndDropZone? LeftZone { get; private set; }
        public EndDropZone? RightZone { get; private set; }

        private TextMeshProUGUI? _label;
        private RectTransform? _tilesContainer;
        private readonly List<TileView> _spawnedTiles = new();

        private void Awake()
        {
            BuildLayout();
        }

        public void Setup(Chain chain, Tile? openingTile = null)
        {
            ClearTiles();

            if (chain.IsEmpty)
            {
                _label!.text = L10n.Get("chain_empty");
                LeftZone!.SetVisible(false);
                RightZone!.SetVisible(false);
                return;
            }

            _label!.text = L10n.Get(
                "chain_open_ends",
                chain.LeftEnd,
                chain.RightEnd,
                chain.Count);

            int openingIdx = FindOpeningIdx(chain, openingTile);
            LayoutResult layout = ComputeLayout(chain, openingIdx);

            for (int i = 0; i < chain.Count; i++)
            {
                PlacedTile pt = chain.Tiles[i];
                TileSlot slot = layout.Slots[i];

                GameObject tileGo = new($"ChainTile_{i}", typeof(RectTransform));
                tileGo.transform.SetParent(_tilesContainer, worldPositionStays: false);
                RectTransform rt = (RectTransform)tileGo.transform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = slot.Center;

                TileView tv = tileGo.AddComponent<TileView>();
                tv.Init(slot.Landscape ? TileOrientation.Landscape : TileOrientation.Portrait);
                tv.Mode = TileInteractionMode.Display;
                tv.Setup(pt.Tile, slot.FirstPip, slot.SecondPip);

                Image? body = tileGo.GetComponent<Image>();
                if (body != null)
                {
                    body.raycastTarget = false;
                }

                _spawnedTiles.Add(tv);
            }

            PositionDropZone(LeftZone!, layout.LeftZoneCenter);
            PositionDropZone(RightZone!, layout.RightZoneCenter);
        }

        // ---- Layout walker ------------------------------------------------

        private readonly struct TileSlot
        {
            public readonly Vector2 Center;
            public readonly bool Landscape;
            public readonly byte FirstPip;
            public readonly byte SecondPip;

            public TileSlot(Vector2 center, bool landscape, byte firstPip, byte secondPip)
            {
                Center = center;
                Landscape = landscape;
                FirstPip = firstPip;
                SecondPip = secondPip;
            }
        }

        private readonly struct LayoutResult
        {
            public readonly TileSlot[] Slots;
            public readonly Vector2 LeftZoneCenter;
            public readonly Vector2 RightZoneCenter;

            public LayoutResult(TileSlot[] slots, Vector2 leftZone, Vector2 rightZone)
            {
                Slots = slots;
                LeftZoneCenter = leftZone;
                RightZoneCenter = rightZone;
            }
        }

        /// <summary>
        /// Locate the opening tile's current index in the chain. As left-end
        /// plays accumulate, the opening shifts to higher indices.
        /// </summary>
        private static int FindOpeningIdx(Chain chain, Tile? openingTile)
        {
            if (!openingTile.HasValue)
            {
                return 0;
            }
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain.Tiles[i].Tile == openingTile.Value)
                {
                    return i;
                }
            }
            return 0;
        }

        private LayoutResult ComputeLayout(Chain chain, int openingIdx)
        {
            // Use a fixed virtual height for the walker so both phones bend
            // at the same chain indices. The actual container is still
            // flex-sized to the parent region; only the *layout math* uses
            // a constant.
            float innerH = VirtualInnerHeight;

            TileSlot[] slots = new TileSlot[chain.Count];

            // Place opening at the vertical center.
            PlacedTile opening = chain.Tiles[openingIdx];
            bool openingLandscape = opening.Tile.IsDouble;
            float openingH = openingLandscape ? TileView.ShortDim : TileView.LongDim;
            float openingCenterY = innerH / 2f;
            float openingCenterX = ColumnCenterX(0);

            slots[openingIdx] = new TileSlot(
                new Vector2(openingCenterX, -(HeadRoom + openingCenterY)),
                openingLandscape,
                opening.LeftPip,
                opening.RightPip);

            // Walk downward from openingIdx+1 to chain.Count-1.
            WalkState down = new()
            {
                Col = 0,
                LastTileCenterY = openingCenterY,
                NextEdgeY = openingCenterY + openingH / 2f + TileSpacing,
                GoingDown = true,
            };
            for (int i = openingIdx + 1; i < chain.Count; i++)
            {
                WalkPlace(chain, slots, i, ref down, innerH);
            }

            // Walk upward from openingIdx-1 down to 0.
            WalkState up = new()
            {
                Col = 0,
                LastTileCenterY = openingCenterY,
                NextEdgeY = openingCenterY - openingH / 2f - TileSpacing,
                GoingDown = false,
            };
            for (int i = openingIdx - 1; i >= 0; i--)
            {
                WalkPlace(chain, slots, i, ref up, innerH);
            }

            // Drop zones — at the running ends of each direction.
            Vector2 leftZone = DropZoneCenter(up, innerH, atTop: true);
            Vector2 rightZone = DropZoneCenter(down, innerH, atTop: false);

            return new LayoutResult(slots, leftZone, rightZone);
        }

        private struct WalkState
        {
            public int Col;
            // Center Y (in inner-area coords) of the most recently placed tile
            // in the current column. Used to align the elbow at bend time.
            public float LastTileCenterY;
            // For goingDown: top edge of where the next tile would be placed.
            // For goingUp:   bottom edge of where the next tile would be placed.
            public float NextEdgeY;
            public bool GoingDown;
            // Toggles each time the snake bends. In the first column of any
            // walk, portrait tiles render with TOP = LeftPip / BOTTOM = RightPip
            // (chain predecessor above, successor below — the "standard"
            // mapping). After a bend, predecessor and successor swap visual
            // sides (predecessor below, successor above), so portrait tiles
            // need the OPPOSITE mapping: TOP = RightPip, BOTTOM = LeftPip.
            // The bridge tile's own pip swap is computed separately at
            // bend-time and isn't affected by this flag.
            public bool PortraitPipsFlipped;
        }

        /// <summary>
        /// Place tile <paramref name="i"/> per <paramref name="state"/>. If
        /// it overflows the column, force-promote it to a landscape "elbow"
        /// at the bend corner, level with the last regular tile of the
        /// outgoing column. Mutates <paramref name="state"/>.
        /// </summary>
        private void WalkPlace(
            Chain chain,
            TileSlot[] slots,
            int i,
            ref WalkState state,
            float innerH)
        {
            PlacedTile pt = chain.Tiles[i];
            bool isDouble = pt.Tile.IsDouble;
            float tentativeH = isDouble ? TileView.ShortDim : TileView.LongDim;

            bool willBend = state.GoingDown
                ? state.NextEdgeY + tentativeH > innerH
                : state.NextEdgeY - tentativeH < 0f;

            bool isLandscape;
            float tileH;
            float tileCenterX;
            float tileCenterY;
            bool isElbow = false;
            // Captures GoingDown at the moment of the bend so we can decide
            // pip swap polarity. Down-walk bends swap (LEFT visual = RightPip
            // because the next chain tile is to the LEFT of the elbow);
            // up-walk bends DON'T swap (LEFT visual = LeftPip because the
            // next chain tile is to the LEFT *and* has a lower chain index).
            bool elbowFromDownWalk = false;

            if (willBend)
            {
                isElbow = true;
                isLandscape = true;
                tileH = TileView.ShortDim;
                elbowFromDownWalk = state.GoingDown;

                // Capture the previous column's last-tile center Y BEFORE
                // we overwrite state.LastTileCenterY with the bridge's Y.
                // The new column's first regular tile lands at this Y so
                // it's level with the previous column's last tile (forming
                // the top edge of the upside-down-U bend).
                float prevColLastCenterY = state.LastTileCenterY;

                // Real-game bridge: the chain line passes THROUGH the
                // bridge's center, so the bridge sits at the SAME Y as the
                // column's last tile (just to its side, never on top of /
                // below it). It's perpendicular (landscape) so the long
                // edge is horizontal, "inside" the previous column tile's
                // vertical extent — the column tile is LongDim tall but
                // the bridge is only ShortDim tall, so the bridge floats
                // in the middle of the column's vertical band.
                //
                //   col K-1 (portrait, 60×120)        bridge (landscape,
                //   at (col K X, prevColLastCenterY)  120×60) at
                //                                    (midpoint X,
                //                                     prevColLastCenterY)
                //
                // The bridge's right edge meets col K-1's left edge; its
                // left edge meets col K+1's right edge. All three tiles
                // form a horizontal end-to-end run at the same Y.
                float colCenterCurrent = ColumnCenterX(state.Col);
                float colCenterNext = ColumnCenterX(state.Col + 1);
                tileCenterX = (colCenterCurrent + colCenterNext) / 2f;
                // Shift the bridge toward the OUTER edge of the column
                // (the edge in the snake direction): bottom of col K-1
                // for a down-walk bend, top of col K-1 for an up-walk
                // bend. This closes the gap between the bridge and the
                // column's outer edge so the bend reads as a flush
                // inverted-U / U cap, not a bar floating mid-column.
                //
                // (LongDim - ShortDim) / 2 = (120 - 60) / 2 = 30 px shift.
                float bridgeShift = (TileView.LongDim - TileView.ShortDim) / 2f;
                tileCenterY = state.GoingDown
                    ? prevColLastCenterY + bridgeShift   // down-walk bend: bridge aligned with col bottom
                    : prevColLastCenterY - bridgeShift;  // up-walk bend: bridge aligned with col top

                state.Col++;
                state.GoingDown = !state.GoingDown;
                state.LastTileCenterY = tileCenterY;
                state.PortraitPipsFlipped = !state.PortraitPipsFlipped;

                // Position the new column's first regular tile at the SAME
                // Y center as the previous column's last tile so the two
                // align as the U's top edge. The bridge sits below (or
                // above) both of them as the U's cap.
                if (state.GoingDown)
                {
                    // Now going down. NextEdgeY (down) = top edge of next
                    // tile = prevCol last center - LongDim/2.
                    state.NextEdgeY = prevColLastCenterY - TileView.LongDim / 2f;
                }
                else
                {
                    // Now going up. NextEdgeY (up) = bottom edge of next
                    // tile = prevCol last center + LongDim/2.
                    state.NextEdgeY = prevColLastCenterY + TileView.LongDim / 2f;
                }
            }
            else
            {
                isLandscape = isDouble;
                tileH = tentativeH;
                tileCenterX = ColumnCenterX(state.Col);
                tileCenterY = state.GoingDown
                    ? state.NextEdgeY + tileH / 2f
                    : state.NextEdgeY - tileH / 2f;

                state.LastTileCenterY = tileCenterY;
                if (state.GoingDown)
                {
                    state.NextEdgeY += tileH + TileSpacing;
                }
                else
                {
                    state.NextEdgeY -= tileH + TileSpacing;
                }
            }

            byte firstPip, secondPip;
            if (isLandscape)
            {
                // Landscape tiles: first panel = LEFT side, second = RIGHT side.
                // For doubles (the IsDouble case) both pips are equal so any
                // assignment is correct. For ELBOW tiles forced into landscape
                // the swap depends on which walk direction triggered the bend:
                //   - Down-walk bend: next chain tile lives in the new column
                //     to the LEFT (higher chain index). The pip facing that
                //     next tile is RightPip, so LEFT visual = RightPip → SWAP.
                //   - Up-walk bend: next walk-order tile (lower chain index)
                //     lives in the new column to the LEFT, but as the chain-
                //     order PREDECESSOR. The pip facing that predecessor is
                //     LeftPip, so LEFT visual = LeftPip → DON'T SWAP.
                if (isElbow && elbowFromDownWalk)
                {
                    firstPip = pt.RightPip;
                    secondPip = pt.LeftPip;
                }
                else
                {
                    firstPip = pt.LeftPip;
                    secondPip = pt.RightPip;
                }
            }
            else
            {
                // Portrait: first panel = TOP, second = BOTTOM. In the first
                // column of any walk (pre-bend) the chain predecessor sits
                // ABOVE this tile, so TOP = LeftPip (the pip matching the
                // predecessor) and BOTTOM = RightPip. After a bend, the
                // predecessor and successor swap visual sides — TOP and
                // BOTTOM pips flip. State.PortraitPipsFlipped tracks this.
                if (state.PortraitPipsFlipped)
                {
                    firstPip = pt.RightPip;
                    secondPip = pt.LeftPip;
                }
                else
                {
                    firstPip = pt.LeftPip;
                    secondPip = pt.RightPip;
                }
            }

            slots[i] = new TileSlot(
                new Vector2(tileCenterX, -(HeadRoom + tileCenterY)),
                isLandscape,
                firstPip,
                secondPip);
        }

        private static Vector2 DropZoneCenter(WalkState state, float innerH, bool atTop)
        {
            float colX = ColumnCenterX(state.Col);
            float zoneY = state.GoingDown
                ? state.NextEdgeY + DropZoneHeight / 2f
                : state.NextEdgeY - DropZoneHeight / 2f;
            return new Vector2(colX, -(HeadRoom + zoneY));
        }

        /// <summary>
        /// Column center X relative to the tiles container's TOP-CENTER
        /// anchor. Column 0 is the horizontal center (X=0); subsequent
        /// columns step leftward by <see cref="ColumnCenterSpacing"/> so
        /// the bridge tile from column N (centered on N's X, LongDim wide)
        /// meets the right edge of column N+1's portrait tiles exactly.
        /// </summary>
        private static float ColumnCenterX(int col)
        {
            return -(col * ColumnCenterSpacing);
        }

        private static void PositionDropZone(EndDropZone zone, Vector2 center)
        {
            RectTransform rt = (RectTransform)zone.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta = new Vector2(DropZoneWidth, DropZoneHeight);
        }

        // ---- Visual scaffolding ------------------------------------------

        private void BuildLayout()
        {
            VerticalLayoutGroup outer = gameObject.AddComponent<VerticalLayoutGroup>();
            outer.childAlignment = TextAnchor.UpperCenter;
            outer.spacing = 4f;
            outer.padding = new RectOffset(8, 8, 8, 8);
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = false;
            outer.childForceExpandHeight = false;

            LayoutElement chainLayout = gameObject.AddComponent<LayoutElement>();
            chainLayout.preferredWidth = 800f;
            chainLayout.preferredHeight = 1400f;
            chainLayout.flexibleWidth = 1f;
            chainLayout.flexibleHeight = 1f;

            _label = CreateLabel();
            _tilesContainer = CreateTilesContainer();

            LeftZone = CreateDropZone(_tilesContainer!, "LeftDropZone", ChainEnd.Left);
            RightZone = CreateDropZone(_tilesContainer!, "RightDropZone", ChainEnd.Right);
        }

        private TextMeshProUGUI CreateLabel()
        {
            GameObject go = new("ChainLabel", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 800f;
            le.preferredHeight = LabelHeight;
            le.flexibleWidth = 1f;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = LabelFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = LabelColor;
            tmp.text = string.Empty;
            return tmp;
        }

        private RectTransform CreateTilesContainer()
        {
            GameObject go = new("Tiles", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 800f;
            le.preferredHeight = 1400f;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 1f;
            return (RectTransform)go.transform;
        }

        private EndDropZone CreateDropZone(Transform parent, string name, ChainEnd end)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            EndDropZone zone = go.AddComponent<EndDropZone>();
            zone.Init(end);
            return zone;
        }

        private void ClearTiles()
        {
            for (int i = _spawnedTiles.Count - 1; i >= 0; i--)
            {
                if (_spawnedTiles[i] != null)
                {
                    Destroy(_spawnedTiles[i].gameObject);
                }
            }
            _spawnedTiles.Clear();
        }
    }
}
