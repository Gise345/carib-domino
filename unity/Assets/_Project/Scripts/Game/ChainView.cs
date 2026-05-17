#nullable enable
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Renders the played chain — label on top with the open ends, then a row
    /// containing the LEFT drop zone, the chain tiles in portrait orientation,
    /// and the RIGHT drop zone. The drop zones live for the lifetime of the
    /// view (their visibility is toggled via <see cref="EndDropZone.SetVisible"/>);
    /// only the chain tiles between them are torn down and rebuilt on
    /// <see cref="Setup"/>.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ChainView : MonoBehaviour
    {
        private const float TileSpacing = 4f;
        private const float LabelFontSize = 22f;
        private const float LabelHeight = 36f;

        private static readonly Color LabelColor = new(0.95f, 0.92f, 0.85f);

        public EndDropZone? LeftZone { get; private set; }
        public EndDropZone? RightZone { get; private set; }

        private TextMeshProUGUI? _label;
        private RectTransform? _tilesContainer;

        private void Awake()
        {
            BuildLayout();
        }

        public void Setup(Chain chain)
        {
            ClearTiles();

            if (chain.IsEmpty)
            {
                _label!.text = L10n.Get("chain_empty");
            }
            else
            {
                _label!.text = L10n.Get(
                    "chain_open_ends",
                    chain.LeftEnd,
                    chain.RightEnd,
                    chain.Count);
            }

            for (int i = 0; i < chain.Count; i++)
            {
                PlacedTile pt = chain.Tiles[i];
                GameObject tileGo = new($"ChainTile_{i}", typeof(RectTransform));
                tileGo.transform.SetParent(_tilesContainer, worldPositionStays: false);
                TileView tv = tileGo.AddComponent<TileView>();
                tv.Init(TileOrientation.Portrait);
                // Display mode: bright (full opacity) but ignores input —
                // chain tiles are visible record only, never playable.
                tv.Mode = TileInteractionMode.Display;
                tv.Setup(pt.Tile);
            }
        }

        private void BuildLayout()
        {
            VerticalLayoutGroup outer = gameObject.AddComponent<VerticalLayoutGroup>();
            outer.childAlignment = TextAnchor.MiddleCenter;
            outer.spacing = 8f;
            outer.padding = new RectOffset(8, 8, 8, 8);
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = false;
            outer.childForceExpandHeight = false;

            LayoutElement chainLayout = gameObject.AddComponent<LayoutElement>();
            chainLayout.preferredHeight = TileView.LongDim + LabelHeight + 24f;
            chainLayout.minHeight = TileView.LongDim;
            chainLayout.preferredWidth = 1600f;
            chainLayout.flexibleWidth = 1f;
            chainLayout.flexibleHeight = 1f;

            _label = CreateLabel();

            // Tile row hosts: [LeftZone] [TilesContainer] [RightZone]
            GameObject tileRow = new("TileRow", typeof(RectTransform));
            tileRow.transform.SetParent(transform, worldPositionStays: false);
            HorizontalLayoutGroup row = tileRow.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 8f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            LayoutElement rowLe = tileRow.AddComponent<LayoutElement>();
            rowLe.preferredHeight = TileView.LongDim + 8f;
            rowLe.minHeight = TileView.LongDim;

            LeftZone = CreateDropZone(tileRow.transform, "LeftDropZone", ChainEnd.Left);
            _tilesContainer = CreateTilesContainer(tileRow.transform);
            RightZone = CreateDropZone(tileRow.transform, "RightDropZone", ChainEnd.Right);
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

        private RectTransform CreateTilesContainer(Transform parent)
        {
            GameObject go = new("Tiles", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            HorizontalLayoutGroup row = go.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = TileSpacing;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
            for (int i = _tilesContainer!.childCount - 1; i >= 0; i--)
            {
                Destroy(_tilesContainer.GetChild(i).gameObject);
            }
        }
    }
}
