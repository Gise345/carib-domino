#nullable enable
using System.Collections;
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
    /// All bend/column geometry lives in the pure, unit-tested
    /// <see cref="Pose.Core.ChainLayout"/>. This view is a thin renderer: it
    /// asks ChainLayout for a <see cref="ChainSlot"/> per tile (in logical,
    /// top-down layout units) and maps each to an anchored position by
    /// negating Y and offsetting by <see cref="HeadRoom"/>.
    ///
    /// Regular tiles render portrait; doubles render landscape (crosswise),
    /// except a double that is the first tile of a column after a bend. The bend
    /// "bridge" tile renders landscape to turn the corner.
    ///
    /// Pip rendering uses <see cref="PlacedTile.LeftPip"/> /
    /// <see cref="PlacedTile.RightPip"/> rather than the canonical Tile.A/B
    /// order, so a [3|5] played on a 5-end shows the 5 facing the chain.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ChainView : MonoBehaviour
    {
        // Vertical inset from the top of the tiles container to the top of the
        // logical layout area. ChainLayout works in a top-down coordinate space
        // starting at 0; this pushes the whole snake down so it clears the label.
        private const float HeadRoom = 80f;
        private const float DropZoneWidth = 200f;
        private const float DropZoneHeight = 140f;

        // Geometry config handed to the pure walker. VirtualInnerHeight is a
        // fixed logical height (not the actual container size) so both phones
        // bend at the same chain indices regardless of screen resolution.
        private const float VirtualInnerHeight = 1700f;
        private static readonly ChainLayout.Config LayoutConfig = new(
            tileSpacing: 2f,
            virtualHeight: VirtualInnerHeight,
            dropZoneHalfHeight: DropZoneHeight / 2f);

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
            ChainLayoutResult layout = ChainLayout.Compute(chain, openingIdx, LayoutConfig);

            for (int i = 0; i < chain.Count; i++)
            {
                PlacedTile pt = chain.Tiles[i];
                ChainSlot slot = layout.Slots[i];

                GameObject tileGo = new($"ChainTile_{i}", typeof(RectTransform));
                tileGo.transform.SetParent(_tilesContainer, worldPositionStays: false);
                RectTransform rt = (RectTransform)tileGo.transform;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = ToAnchored(slot.CenterX, slot.CenterY);

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

            PositionDropZone(LeftZone!, ToAnchored(layout.LeftZoneX, layout.LeftZoneY));
            PositionDropZone(RightZone!, ToAnchored(layout.RightZoneX, layout.RightZoneY));
        }

        /// <summary>
        /// Maps a logical layout point (X rightward, Y downward from the top of
        /// the layout area) to a tiles-container anchored position. The
        /// container anchors at top-center, so Y is negated and offset by
        /// <see cref="HeadRoom"/>.
        /// </summary>
        private static Vector2 ToAnchored(float layoutX, float layoutY)
        {
            return new Vector2(layoutX, -(HeadRoom + layoutY));
        }

        // ---- Layout helpers ----------------------------------------------

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
            if (_mashRoutine != null)
            {
                StopCoroutine(_mashRoutine);
                _mashRoutine = null;
            }
            for (int i = _spawnedTiles.Count - 1; i >= 0; i--)
            {
                if (_spawnedTiles[i] != null)
                {
                    Destroy(_spawnedTiles[i].gameObject);
                }
            }
            _spawnedTiles.Clear();
        }

        // "Mash up the board" — the celebratory flourish for a KEY win. Flings
        // the laid chain tiles outward with a quick tumbling burst. Purely
        // cosmetic and transient: the next deal rebuilds the chain from scratch
        // (Setup → ClearTiles), so no gameplay state is disturbed. Cosmetic-only
        // randomness, so the default RNG is fine (per the RNG rule).
        private Coroutine? _mashRoutine;

        /// <summary>Scatters the currently-laid chain tiles for a KEY celebration.</summary>
        public void MashUp()
        {
            if (!isActiveAndEnabled || _spawnedTiles.Count == 0)
            {
                return;
            }
            if (_mashRoutine != null)
            {
                StopCoroutine(_mashRoutine);
            }
            _mashRoutine = StartCoroutine(MashUpRoutine());
        }

        private IEnumerator MashUpRoutine()
        {
            int n = _spawnedTiles.Count;
            RectTransform[] rts = new RectTransform[n];
            Vector2[] starts = new Vector2[n];
            Vector2[] targets = new Vector2[n];
            float[] spins = new float[n];
            for (int i = 0; i < n; i++)
            {
                rts[i] = (RectTransform)_spawnedTiles[i].transform;
                starts[i] = rts[i].anchoredPosition;
                // Fling outward from the container centre, plus a random flourish.
                Vector2 outward = starts[i].sqrMagnitude > 1f ? starts[i].normalized : Vector2.up;
                float dist = Random.Range(220f, 480f);
                Vector2 jitter = new(Random.Range(-120f, 120f), Random.Range(-120f, 120f));
                targets[i] = starts[i] + (outward * dist) + jitter;
                spins[i] = Random.Range(-360f, 360f);
            }

            const float duration = 0.9f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float ease = 1f - ((1f - k) * (1f - k)); // ease-out quad
                for (int i = 0; i < n; i++)
                {
                    if (rts[i] == null)
                    {
                        continue;
                    }
                    rts[i].anchoredPosition = Vector2.LerpUnclamped(starts[i], targets[i], ease);
                    rts[i].localRotation = Quaternion.Euler(0f, 0f, spins[i] * ease);
                }
                yield return null;
            }
            _mashRoutine = null;
        }
    }
}
