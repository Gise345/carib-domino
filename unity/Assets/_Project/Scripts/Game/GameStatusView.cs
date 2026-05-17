#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Footer below the hands: shows a status line (formatted by the caller —
    /// "Your turn — alice", "Waiting for bob…", or a round-over message) and
    /// exposes a "Pass turn" button enabled only when the caller says passing
    /// is legal *and* it's actually the human's turn.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class GameStatusView : MonoBehaviour
    {
        private const float StatusFontSize = 26f;
        private const float ButtonWidth = 200f;
        private const float ButtonHeight = 60f;
        private const float ButtonFontSize = 24f;

        private static readonly Color StatusColor = new(0.95f, 0.92f, 0.85f);
        private static readonly Color ButtonColor = new(0.18f, 0.42f, 0.28f);
        private static readonly Color ButtonDisabledColor = new(0.18f, 0.42f, 0.28f, 0.4f);
        private static readonly Color ButtonTextColor = new(0.97f, 0.95f, 0.88f);
        private static readonly Color OutcomeColor = new(1.0f, 0.92f, 0.50f);

        public event Action? PassClicked;

        private TextMeshProUGUI? _statusLabel;
        private Button? _passButton;
        private Image? _passButtonImage;

        private void Awake()
        {
            BuildLayout();
        }

        public void Setup(string statusText, bool passEnabled, bool isOver)
        {
            _statusLabel!.text = statusText;
            _statusLabel.color = isOver ? OutcomeColor : StatusColor;
            _statusLabel.fontStyle = isOver ? FontStyles.Bold : FontStyles.Normal;
            SetPassEnabled(passEnabled);
        }

        private void SetPassEnabled(bool enabled)
        {
            _passButton!.interactable = enabled;
            _passButtonImage!.color = enabled ? ButtonColor : ButtonDisabledColor;
        }

        private void BuildLayout()
        {
            HorizontalLayoutGroup hlg = gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 24f;
            hlg.padding = new RectOffset(8, 8, 8, 8);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement footerLayout = gameObject.AddComponent<LayoutElement>();
            footerLayout.preferredHeight = ButtonHeight + 16f;
            footerLayout.minHeight = ButtonHeight;
            footerLayout.preferredWidth = 1000f;
            footerLayout.flexibleWidth = 1f;

            _statusLabel = CreateStatusLabel();
            (_passButton, _passButtonImage) = CreatePassButton();
            _passButton.onClick.AddListener(OnPassButtonClicked);
        }

        private TextMeshProUGUI CreateStatusLabel()
        {
            GameObject go = new("StatusLabel", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 600f;
            le.preferredHeight = ButtonHeight;
            le.flexibleWidth = 1f;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontSize = StatusFontSize;
            tmp.color = StatusColor;
            tmp.text = string.Empty;
            return tmp;
        }

        private (Button button, Image image) CreatePassButton()
        {
            GameObject go = new("PassButton", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;

            Image img = go.AddComponent<Image>();
            img.color = ButtonColor;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            GameObject labelGo = new("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TextMeshProUGUI labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.fontSize = ButtonFontSize;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = ButtonTextColor;
            labelTmp.text = L10n.Get("pass_button");
            labelTmp.raycastTarget = false;

            return (btn, img);
        }

        private void OnPassButtonClicked()
        {
            PassClicked?.Invoke();
        }
    }
}
