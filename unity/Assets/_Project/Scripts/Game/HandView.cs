#nullable enable
using System;
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Layout direction for a hand. <see cref="Horizontal"/> arranges name +
    /// tiles row left-to-right (top and bottom seats). <see cref="Vertical"/>
    /// stacks name above a column of tiles (left and right seats).
    /// </summary>
    public enum HandOrientation
    {
        Horizontal,
        Vertical,
    }

    /// <summary>
    /// Renders one player's hand. Tiles' interaction mode is decided by the
    /// caller via the <c>tileMode</c> predicate passed to <see cref="Setup"/> —
    /// the predicate maps each tile to its <see cref="TileInteractionMode"/>
    /// (None, Display, Click, or Drag). Bubbles up <see cref="TileClicked"/>,
    /// <see cref="TileDragStarted"/>, <see cref="TileDragEnded"/> events.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class HandView : MonoBehaviour
    {
        private const float NameFontSize = 28f;
        private const float TileSpacing = 6f;
        private const float NameLabelWidthHorizontal = 120f;
        private const float NameLabelHeightVertical = 40f;

        public event Action<TileView>? TileClicked;
        public event Action<TileView>? TileDragStarted;
        public event Action<TileView>? TileDragEnded;

        private HandOrientation _handOrientation = HandOrientation.Horizontal;
        private TileOrientation _tileOrientation = TileOrientation.Portrait;
        private bool _layoutBuilt;

        private TextMeshProUGUI? _nameLabel;
        private RectTransform? _tilesContainer;

        public void Init(HandOrientation handOrientation, TileOrientation tileOrientation)
        {
            if (_layoutBuilt)
            {
                return;
            }
            _handOrientation = handOrientation;
            _tileOrientation = tileOrientation;
        }

        public void Setup(
            string playerName,
            bool isCurrent,
            Hand hand,
            Func<Tile, TileInteractionMode>? tileMode = null)
        {
            EnsureLayoutBuilt();

            _nameLabel!.text = isCurrent ? $"{playerName} *" : playerName;
            _nameLabel.fontStyle = isCurrent ? FontStyles.Bold : FontStyles.Normal;

            for (int i = _tilesContainer!.childCount - 1; i >= 0; i--)
            {
                Destroy(_tilesContainer.GetChild(i).gameObject);
            }

            foreach (Tile t in hand)
            {
                GameObject tileGo = new("Tile", typeof(RectTransform));
                tileGo.transform.SetParent(_tilesContainer, worldPositionStays: false);
                TileView tv = tileGo.AddComponent<TileView>();
                tv.Init(_tileOrientation);
                tv.Mode = tileMode != null ? tileMode(t) : TileInteractionMode.None;
                tv.Setup(t);
                tv.Clicked += OnTileClickedInternal;
                tv.DragStarted += OnTileDragStartedInternal;
                tv.DragEnded += OnTileDragEndedInternal;
            }
        }

        private void OnTileClickedInternal(TileView tv) => TileClicked?.Invoke(tv);
        private void OnTileDragStartedInternal(TileView tv) => TileDragStarted?.Invoke(tv);
        private void OnTileDragEndedInternal(TileView tv) => TileDragEnded?.Invoke(tv);

        private void EnsureLayoutBuilt()
        {
            if (_layoutBuilt)
            {
                return;
            }
            BuildLayout();
            _layoutBuilt = true;
        }

        private void BuildLayout()
        {
            if (_handOrientation == HandOrientation.Horizontal)
            {
                BuildHorizontalLayout();
            }
            else
            {
                BuildVerticalLayout();
            }
        }

        private void BuildHorizontalLayout()
        {
            HorizontalLayoutGroup outer = gameObject.AddComponent<HorizontalLayoutGroup>();
            outer.childAlignment = TextAnchor.MiddleCenter;
            outer.spacing = 12f;
            outer.padding = new RectOffset(8, 8, 4, 4);
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = false;
            outer.childForceExpandHeight = false;

            float tileH = _tileOrientation == TileOrientation.Portrait
                ? TileView.LongDim
                : TileView.ShortDim;

            LayoutElement rowLayout = gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = tileH + 8f;
            rowLayout.minHeight = tileH;
            rowLayout.preferredWidth = 1000f;
            rowLayout.flexibleWidth = 1f;

            _nameLabel = CreateNameLabel(
                preferredWidth: NameLabelWidthHorizontal,
                preferredHeight: tileH,
                alignment: TextAlignmentOptions.MidlineLeft);

            _tilesContainer = CreateTilesContainer(asRow: true);
        }

        private void BuildVerticalLayout()
        {
            VerticalLayoutGroup outer = gameObject.AddComponent<VerticalLayoutGroup>();
            outer.childAlignment = TextAnchor.UpperCenter;
            outer.spacing = 8f;
            outer.padding = new RectOffset(4, 4, 8, 8);
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = false;
            outer.childForceExpandHeight = false;

            float tileW = _tileOrientation == TileOrientation.Portrait
                ? TileView.ShortDim
                : TileView.LongDim;

            LayoutElement colLayout = gameObject.AddComponent<LayoutElement>();
            colLayout.preferredWidth = tileW + 16f;
            colLayout.minWidth = tileW;
            colLayout.preferredHeight = 1000f;
            colLayout.flexibleHeight = 1f;

            _nameLabel = CreateNameLabel(
                preferredWidth: tileW + 16f,
                preferredHeight: NameLabelHeightVertical,
                alignment: TextAlignmentOptions.Center);

            _tilesContainer = CreateTilesContainer(asRow: false);
        }

        private TextMeshProUGUI CreateNameLabel(
            float preferredWidth,
            float preferredHeight,
            TextAlignmentOptions alignment)
        {
            GameObject go = new("Name", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.preferredHeight = preferredHeight;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = alignment;
            tmp.fontSize = NameFontSize;
            tmp.color = Color.white;
            tmp.text = string.Empty;
            return tmp;
        }

        private RectTransform CreateTilesContainer(bool asRow)
        {
            GameObject go = new("Tiles", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            HorizontalOrVerticalLayoutGroup row;
            if (asRow)
            {
                HorizontalLayoutGroup hlg = go.AddComponent<HorizontalLayoutGroup>();
                hlg.childAlignment = TextAnchor.MiddleLeft;
                row = hlg;
            }
            else
            {
                VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperCenter;
                row = vlg;
            }
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
    }
}
