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

        // Tile size for this seat. The local hand is larger than the others.
        private float _shortDim = TileView.ShortDim;
        private float _longDim = TileView.LongDim;

        // Centre-to-centre step between tiles in a horizontal hand. Null lays
        // them out side by side with TileSpacing; a value smaller than the tile
        // width fans them so each laps the one before it.
        private float? _fanStep;

        // The local hand hides its name plate — the corner profile already
        // says who you are, and the label costs width the fanned hand needs.
        private bool _showName = true;

        private bool _layoutBuilt;

        private TextMeshProUGUI? _nameLabel;
        private RectTransform? _tilesContainer;

        // Name-plate tint, re-applied on every Setup so it survives a hand
        // rebuild. White by default; team games (Jamaican Partner) set a per-team
        // colour via SetAccentColor.
        private Color _accentColor = Color.white;

        /// <summary>
        /// Sets the seat's name-plate colour, used to signal team membership in
        /// partner games. Takes effect on the next <see cref="Setup"/>; pass
        /// <see cref="Color.white"/> to clear (the Cut-Throat default).
        /// </summary>
        public void SetAccentColor(Color color)
        {
            _accentColor = color;
            if (_nameLabel != null)
            {
                _nameLabel.color = color;
            }
        }

        /// <summary>
        /// Fixes this seat's layout. Must be called before the first
        /// <see cref="Setup"/>.
        /// </summary>
        /// <param name="handOrientation">Row (top/bottom) or column (sides).</param>
        /// <param name="tileOrientation">Portrait or landscape tiles.</param>
        /// <param name="shortDim">Tile short side; see <see cref="TileView.LocalShortDim"/>.</param>
        /// <param name="longDim">Tile long side.</param>
        /// <param name="fanStep">
        /// Centre-to-centre spacing for a horizontal hand. Pass a value smaller
        /// than the tile width to fan the tiles; null lays them side by side.
        /// </param>
        /// <param name="showName">
        /// False to drop the name plate — used for the local hand, whose name
        /// is already on the corner profile.
        /// </param>
        public void Init(
            HandOrientation handOrientation,
            TileOrientation tileOrientation,
            float shortDim = TileView.ShortDim,
            float longDim = TileView.LongDim,
            float? fanStep = null,
            bool showName = true)
        {
            if (_layoutBuilt)
            {
                return;
            }
            _handOrientation = handOrientation;
            _tileOrientation = tileOrientation;
            _shortDim = shortDim;
            _longDim = longDim;
            _fanStep = fanStep;
            _showName = showName;
        }

        public void Setup(
            string playerName,
            bool isCurrent,
            Hand hand,
            Func<Tile, TileInteractionMode>? tileMode = null,
            bool showBacks = false)
        {
            EnsureLayoutBuilt();

            if (_nameLabel != null)
            {
                _nameLabel.text = isCurrent ? $"{playerName} *" : playerName;
                _nameLabel.fontStyle = isCurrent ? FontStyles.Bold : FontStyles.Normal;
                _nameLabel.color = _accentColor;
            }

            for (int i = _tilesContainer!.childCount - 1; i >= 0; i--)
            {
                Destroy(_tilesContainer.GetChild(i).gameObject);
            }

            // Online opponent: render N face-down tiles to convey hand size
            // without leaking which tiles they hold. The hand parameter is
            // still passed because Count is what we render — identity ignored.
            if (showBacks)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    GameObject tileGo = new("TileBack", typeof(RectTransform));
                    tileGo.transform.SetParent(_tilesContainer, worldPositionStays: false);
                    TileView tv = tileGo.AddComponent<TileView>();
                    tv.Init(_tileOrientation, _shortDim, _longDim);
                    // Backs light up on their owner's turn and dim otherwise —
                    // a second read on whose turn it is, alongside the seat
                    // glow. Display rather than None because these are never
                    // interactive, only bright.
                    tv.Mode = isCurrent ? TileInteractionMode.Display : TileInteractionMode.None;
                    tv.SetupAsBack();
                }
                return;
            }

            foreach (Tile t in hand)
            {
                GameObject tileGo = new("Tile", typeof(RectTransform));
                tileGo.transform.SetParent(_tilesContainer, worldPositionStays: false);
                TileView tv = tileGo.AddComponent<TileView>();
                tv.Init(_tileOrientation, _shortDim, _longDim);
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
                ? _longDim
                : _shortDim;

            LayoutElement rowLayout = gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = tileH + 8f;
            rowLayout.minHeight = tileH;
            rowLayout.preferredWidth = 1000f;
            rowLayout.flexibleWidth = 1f;

            if (_showName)
            {
                _nameLabel = CreateNameLabel(
                    preferredWidth: NameLabelWidthHorizontal,
                    preferredHeight: tileH,
                    alignment: TextAlignmentOptions.MidlineLeft);
            }

            _tilesContainer = CreateTilesContainer(asRow: true);
        }

        private void BuildVerticalLayout()
        {
            VerticalLayoutGroup outer = gameObject.AddComponent<VerticalLayoutGroup>();
            outer.childAlignment = TextAnchor.MiddleCenter; // centre the side hand vertically
            outer.spacing = 8f;
            outer.padding = new RectOffset(4, 4, 8, 8);
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = false;
            outer.childForceExpandHeight = false;

            float tileW = _tileOrientation == TileOrientation.Portrait
                ? _shortDim
                : _longDim;

            LayoutElement colLayout = gameObject.AddComponent<LayoutElement>();
            colLayout.preferredWidth = tileW + 16f;
            colLayout.minWidth = tileW;
            colLayout.preferredHeight = 1000f;
            colLayout.flexibleHeight = 1f;

            if (_showName)
            {
                _nameLabel = CreateNameLabel(
                    preferredWidth: tileW + 16f,
                    preferredHeight: NameLabelHeightVertical,
                    alignment: TextAlignmentOptions.Center);
            }

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
            // A fan step smaller than the tile width becomes negative spacing,
            // so each tile laps the one before it. Later siblings draw on top,
            // and tiles are added left to right, so the lap falls on each
            // tile's right edge — the outer pip column loses a few units, and
            // the selected tile lifts clear of its neighbours anyway.
            if (asRow && _fanStep.HasValue)
            {
                float tileW = _tileOrientation == TileOrientation.Portrait
                    ? _shortDim
                    : _longDim;
                row.spacing = _fanStep.Value - tileW;
            }
            else
            {
                row.spacing = TileSpacing;
            }
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
