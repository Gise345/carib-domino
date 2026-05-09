#nullable enable
using System;
using Pose.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Footer below the hands: shows whose turn it is, exposes a "Pass turn"
    /// button (enabled only when passing is the player's only legal move), and
    /// switches to a round-over message once <see cref="MatchState.IsOver"/>.
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

        public void Setup(MatchState state, MatchOutcome? outcome, bool passIsLegal)
        {
            if (state.IsOver && outcome != null)
            {
                _statusLabel!.color = OutcomeColor;
                _statusLabel.fontStyle = FontStyles.Bold;
                _statusLabel.text = FormatOutcome(outcome);
                SetPassEnabled(false);
                return;
            }

            _statusLabel!.color = StatusColor;
            _statusLabel.fontStyle = FontStyles.Normal;
            _statusLabel.text =
                $"Turn {state.TurnNumber} — {state.CurrentPlayer.Value} to play";
            SetPassEnabled(passIsLegal);
        }

        private static string FormatOutcome(MatchOutcome outcome)
        {
            string reason = outcome.Reason switch
            {
                MatchEndReason.Domino => "Domino",
                MatchEndReason.Blocked => "Block",
                _ => outcome.Reason.ToString(),
            };

            if (outcome.IsDraw)
            {
                return $"Round over — {reason}, draw (no score)";
            }

            return $"Round over — {reason}, {outcome.WinnerId!.Value.Value} wins +{outcome.WinnerScore}";
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

            // Label child centred inside the button.
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
            labelTmp.text = "Pass turn";
            labelTmp.raycastTarget = false;

            return (btn, img);
        }

        private void OnPassButtonClicked()
        {
            PassClicked?.Invoke();
        }
    }
}
