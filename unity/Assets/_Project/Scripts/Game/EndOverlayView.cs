#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Modal overlay shown when a round finishes or the opponent leaves. Covers
    /// the whole board with a dimmed, raycast-blocking backdrop so a stray tap
    /// can't reach tiles underneath, and presents a title, an optional subtitle
    /// (used for "your opponent wants a rematch") and up to two buttons.
    ///
    /// The view is deliberately dumb: callers supply already-localized strings
    /// and decide what the buttons mean. <see cref="PrimaryClicked"/> is the
    /// affirmative action (Play again / Rematch), <see cref="SecondaryClicked"/>
    /// the exit (Back to lobby).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class EndOverlayView : MonoBehaviour
    {
        private const float TitleFontSize = 40f;
        private const float SubtitleFontSize = 24f;
        private const float ButtonFontSize = 26f;
        private const float ButtonWidth = 260f;
        private const float ButtonHeight = 68f;
        private const float PanelWidth = 620f;
        private const float PanelPadding = 32f;
        private const float PanelSpacing = 20f;
        private const float ButtonRowSpacing = 24f;

        private static readonly Color BackdropColor = new(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelColor = new(0.08f, 0.20f, 0.14f, 0.98f);
        private static readonly Color TitleColor = new(1.0f, 0.92f, 0.50f);
        private static readonly Color SubtitleColor = new(0.85f, 0.88f, 0.82f);
        private static readonly Color PrimaryColor = new(0.18f, 0.42f, 0.28f);
        private static readonly Color PrimaryDisabledColor = new(0.18f, 0.42f, 0.28f, 0.4f);
        private static readonly Color SecondaryColor = new(0.32f, 0.20f, 0.18f);
        private static readonly Color ButtonTextColor = new(0.97f, 0.95f, 0.88f);

        /// <summary>Affirmative action — "Play again" offline, "Rematch" online.</summary>
        public event Action? PrimaryClicked;

        /// <summary>Exit action — "Back to lobby".</summary>
        public event Action? SecondaryClicked;

        private TextMeshProUGUI? _titleLabel;
        private TextMeshProUGUI? _subtitleLabel;

        private Button? _primaryButton;
        private Image? _primaryImage;
        private TextMeshProUGUI? _primaryLabel;

        private Button? _secondaryButton;
        private TextMeshProUGUI? _secondaryLabel;

        private void Awake()
        {
            BuildLayout();
            Hide();
        }

        /// <summary>
        /// Shows the overlay. A null <paramref name="primaryLabel"/> hides the
        /// affirmative button entirely — used when there is nothing to affirm
        /// (the opponent has left).
        /// </summary>
        /// <param name="title">Localized headline, e.g. the round outcome.</param>
        /// <param name="subtitle">Localized secondary line, or null to hide it.</param>
        /// <param name="primaryLabel">Localized affirmative label, or null to hide the button.</param>
        /// <param name="primaryInteractable">
        /// False to show the primary button greyed — e.g. "Waiting for opponent…"
        /// after this client has already voted for a rematch.
        /// </param>
        /// <param name="secondaryLabel">Localized exit label.</param>
        public void Show(
            string title,
            string? subtitle,
            string? primaryLabel,
            bool primaryInteractable,
            string? secondaryLabel)
        {
            _titleLabel!.text = title;

            if (string.IsNullOrEmpty(subtitle))
            {
                _subtitleLabel!.gameObject.SetActive(false);
            }
            else
            {
                _subtitleLabel!.gameObject.SetActive(true);
                _subtitleLabel.text = subtitle;
            }

            if (primaryLabel == null)
            {
                _primaryButton!.gameObject.SetActive(false);
            }
            else
            {
                _primaryButton!.gameObject.SetActive(true);
                _primaryLabel!.text = primaryLabel;
                _primaryButton.interactable = primaryInteractable;
                _primaryImage!.color = primaryInteractable ? PrimaryColor : PrimaryDisabledColor;
            }

            // Hide the secondary button entirely when there's no label — e.g. the
            // series between-rounds popup that auto-advances with no exit.
            if (string.IsNullOrEmpty(secondaryLabel))
            {
                _secondaryButton!.gameObject.SetActive(false);
            }
            else
            {
                _secondaryButton!.gameObject.SetActive(true);
                _secondaryLabel!.text = secondaryLabel;
            }

            gameObject.SetActive(true);
            // The overlay is built once and reused; siblings added afterwards
            // (drag ghosts, chain tiles) would otherwise draw over it.
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>Updates just the subtitle line (used for the between-rounds countdown).</summary>
        public void SetSubtitle(string subtitle)
        {
            if (_subtitleLabel == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(subtitle))
            {
                _subtitleLabel.gameObject.SetActive(false);
            }
            else
            {
                _subtitleLabel.gameObject.SetActive(true);
                _subtitleLabel.text = subtitle;
            }
        }

        /// <summary>True while the overlay is covering the board.</summary>
        public bool IsShowing => gameObject.activeSelf;

        private void BuildLayout()
        {
            RectTransform rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Blocks input to the board underneath.
            Image backdrop = gameObject.AddComponent<Image>();
            backdrop.color = BackdropColor;
            backdrop.raycastTarget = true;

            RectTransform panel = CreatePanel();
            _titleLabel = CreateTitleLabel(panel);
            _subtitleLabel = CreateSubtitleLabel(panel);

            RectTransform buttonRow = CreateButtonRow(panel);
            (_primaryButton, _primaryImage, _primaryLabel) =
                CreateButton(buttonRow, "PrimaryButton", PrimaryColor);
            (_secondaryButton, _, _secondaryLabel) =
                CreateButton(buttonRow, "SecondaryButton", SecondaryColor);

            _primaryButton.onClick.AddListener(() => PrimaryClicked?.Invoke());
            _secondaryButton.onClick.AddListener(() => SecondaryClicked?.Invoke());
        }

        private RectTransform CreatePanel()
        {
            GameObject go = new("Panel", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(PanelWidth, 0f);

            Image bg = go.AddComponent<Image>();
            bg.color = PanelColor;

            VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = PanelSpacing;
            vlg.padding = new RectOffset(
                (int)PanelPadding, (int)PanelPadding, (int)PanelPadding, (int)PanelPadding);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Height tracks the (variable) content: the subtitle and primary
            // button come and go depending on which state we're showing.
            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            return rt;
        }

        private static TextMeshProUGUI CreateTitleLabel(RectTransform parent)
        {
            GameObject go = new("Title", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = TitleFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = TitleColor;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static TextMeshProUGUI CreateSubtitleLabel(RectTransform parent)
        {
            GameObject go = new("Subtitle", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = SubtitleFontSize;
            tmp.color = SubtitleColor;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static RectTransform CreateButtonRow(RectTransform parent)
        {
            GameObject go = new("ButtonRow", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            HorizontalLayoutGroup hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = ButtonRowSpacing;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = ButtonHeight;
            le.minHeight = ButtonHeight;

            return (RectTransform)go.transform;
        }

        private static (Button button, Image image, TextMeshProUGUI label) CreateButton(
            RectTransform parent,
            string name,
            Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;

            Image img = go.AddComponent<Image>();
            img.color = color;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            GameObject labelGo = new("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = ButtonFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = ButtonTextColor;
            tmp.raycastTarget = false;

            return (btn, img, tmp);
        }
    }
}
