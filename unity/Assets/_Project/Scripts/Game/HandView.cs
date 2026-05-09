#nullable enable
using System;
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Renders one player's hand as a horizontal strip: name label on the left,
    /// then a row of <see cref="TileView"/> instances for each tile held. An
    /// asterisk after the name marks the current player. Dim, non-interactable
    /// tiles for non-current players (or for the current player's tiles that
    /// can't legally be played); bright, interactable tiles for the current
    /// player's playable tiles. Bubbles up <see cref="TileClicked"/> when any
    /// of its tiles is tapped.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class HandView : MonoBehaviour
    {
        private const float NameLabelWidth = 120f;
        private const float NameFontSize = 28f;
        private const float TileSpacing = 6f;

        public event Action<Tile>? TileClicked;

        private TextMeshProUGUI? _nameLabel;
        private RectTransform? _tilesContainer;

        private void Awake()
        {
            BuildLayout();
        }

        /// <summary>
        /// Re-renders the hand. <paramref name="isTilePlayable"/> is consulted for
        /// each tile to decide whether it should render interactable (bright) or
        /// dimmed. Pass <c>null</c> to render every tile dim/non-interactable
        /// (used for non-current players).
        /// </summary>
        public void Setup(
            string playerName,
            bool isCurrent,
            Hand hand,
            Func<Tile, bool>? isTilePlayable = null)
        {
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
                tv.Setup(t);
                tv.Interactable = isTilePlayable != null && isTilePlayable(t);
                tv.Clicked += OnTileViewClicked;
            }
        }

        private void OnTileViewClicked(Tile tile)
        {
            TileClicked?.Invoke(tile);
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

            _nameLabel = CreateNameLabel();
            _tilesContainer = CreateTilesContainer();
        }

        private TextMeshProUGUI CreateNameLabel()
        {
            GameObject go = new("Name", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = NameLabelWidth;
            le.preferredHeight = TileView.Height;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontSize = NameFontSize;
            tmp.color = Color.white;
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
    }
}
