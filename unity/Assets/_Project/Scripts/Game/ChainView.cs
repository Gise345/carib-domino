#nullable enable
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Renders the played chain as a horizontal row of <see cref="TileView"/>
    /// instances. Each chain tile is shown in the same portrait orientation as
    /// hand tiles for the M1 step 4 spike — proper chain orientation (landscape
    /// tiles, doubles rotated 90°, chain bending at corners) is a polish slice
    /// for later when the spatial table layout lands.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ChainView : MonoBehaviour
    {
        private const float TileSpacing = 4f;
        private const float LabelFontSize = 24f;
        private const float LabelWidth = 140f;

        private static readonly Color LabelColor = new(0.95f, 0.92f, 0.85f);

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
                _label!.text = "Chain: empty";
                return;
            }

            _label!.text = $"Chain ({chain.Count}):";
            for (int i = 0; i < chain.Count; i++)
            {
                PlacedTile pt = chain.Tiles[i];
                GameObject tileGo = new($"ChainTile_{i}", typeof(RectTransform));
                tileGo.transform.SetParent(_tilesContainer, worldPositionStays: false);
                TileView tv = tileGo.AddComponent<TileView>();
                tv.Setup(pt.Tile);
            }
        }

        private void BuildLayout()
        {
            HorizontalLayoutGroup outer = gameObject.AddComponent<HorizontalLayoutGroup>();
            outer.childAlignment = TextAnchor.MiddleLeft;
            outer.spacing = 12f;
            outer.padding = new RectOffset(8, 8, 4, 4);
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = false;
            outer.childForceExpandHeight = false;

            LayoutElement rowLayout = gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = TileView.Height + 8f;
            rowLayout.minHeight = TileView.Height;
            rowLayout.preferredWidth = 1000f;
            rowLayout.flexibleWidth = 1f;

            _label = CreateLabel();
            _tilesContainer = CreateTilesContainer();
        }

        private TextMeshProUGUI CreateLabel()
        {
            GameObject go = new("ChainLabel", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = LabelWidth;
            le.preferredHeight = TileView.Height;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
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

            HorizontalLayoutGroup row = go.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
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

        private void ClearTiles()
        {
            for (int i = _tilesContainer!.childCount - 1; i >= 0; i--)
            {
                Destroy(_tilesContainer.GetChild(i).gameObject);
            }
        }
    }
}
