#nullable enable
using System;
using Pose.Core;
using Pose.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Full-screen lobby panel shown at app start. Three actions:
    /// <list type="bullet">
    ///   <item><b>Practice vs Bots</b> — dismisses the lobby and lets
    ///         <see cref="BoardBootstrap"/> deal the existing offline 4-player
    ///         Cut-Throat scene with three <see cref="RandomBot"/> opponents.</item>
    ///   <item><b>Create Room</b> — generates a 6-char code via
    ///         <see cref="RoomCodeGenerator"/>, calls
    ///         <see cref="PhotonBootstrap.CreateRoom"/>, displays the code so
    ///         the player can share it. M3.2 stops here — the actual networked
    ///         gameplay lands in M3.3.</item>
    ///   <item><b>Join Room</b> — text input + Join button; calls
    ///         <see cref="PhotonBootstrap.JoinRoom"/> with the entered code.</item>
    /// </list>
    /// Self-built (no editor wiring), matches the felt-green / ivory palette.
    /// Bubbles <see cref="PracticeChosen"/> and <see cref="OnlineRoomActive"/>
    /// for <see cref="BoardBootstrap"/> to consume.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class LobbyView : MonoBehaviour
    {
        // Palette mirrors the existing GameStatusView / TileView choices.
        private static readonly Color PanelColor = new(0.05f, 0.30f, 0.18f, 0.97f);
        private static readonly Color CardColor = new(0.10f, 0.40f, 0.24f, 1f);
        private static readonly Color ButtonColor = new(0.97f, 0.95f, 0.88f);
        private static readonly Color ButtonDisabledColor = new(0.97f, 0.95f, 0.88f, 0.45f);
        private static readonly Color ButtonTextColor = new(0.05f, 0.30f, 0.18f);
        private static readonly Color BodyTextColor = new(0.97f, 0.95f, 0.88f);
        private static readonly Color CodeTextColor = new(1.0f, 0.92f, 0.50f);
        private static readonly Color InputBgColor = new(0.04f, 0.20f, 0.12f);
        private static readonly Color StatusErrorColor = new(1.0f, 0.55f, 0.45f);

        private const float TitleFontSize = 56f;
        private const float ButtonFontSize = 28f;
        private const float BodyFontSize = 24f;
        private const float CodeFontSize = 72f;
        private const float ButtonWidth = 400f;
        private const float ButtonHeight = 80f;

        public event Action? PracticeChosen;

        /// <summary>
        /// Fires with the room code and, for the creator, the chosen player count
        /// (2–4) and game mode. Joiners pass count 0 and <see cref="GameMode.CutThroat"/>
        /// as placeholders — the real values arrive from the host over the network.
        /// </summary>
        public event Action<string, int, GameMode>? OnlineRoomActive;

        // The action buttons and pickers (kept around so we can disable / hide
        // them when transitioning to the connected state).
        private GameObject? _practiceButton;
        private GameObject? _createButton;
        private GameObject? _modePickerRow;
        private GameObject? _countPickerRow;
        private GameObject? _joinButton;
        private GameObject? _joinInputRow;

        private TMP_InputField? _codeInput;
        private TextMeshProUGUI? _statusText;
        private TextMeshProUGUI? _codeDisplay;

        private bool _busy;
        private Image? _backgroundImage;

        private void Awake()
        {
            BuildLayout();
        }

        /// <summary>
        /// Applies a sprite to the lobby's full-screen background. When non-null
        /// the sprite is shown un-tinted (color reset to white) and stretched
        /// across the panel; when null the original felt-green PanelColor is
        /// used. Call after the LobbyView is added — typically wired by
        /// BoardBootstrap from a SerializeField on the bootstrap GameObject so
        /// the artwork stays editor-driven.
        /// </summary>
        public void SetBackgroundSprite(Sprite? sprite)
        {
            if (_backgroundImage == null)
            {
                return;
            }
            if (sprite == null)
            {
                _backgroundImage.sprite = null;
                _backgroundImage.color = PanelColor;
                _backgroundImage.type = Image.Type.Simple;
                return;
            }
            _backgroundImage.sprite = sprite;
            _backgroundImage.color = Color.white;
            _backgroundImage.type = Image.Type.Simple;
            _backgroundImage.preserveAspect = false;
        }

        // ---- UI build ------------------------------------------------------

        private void BuildLayout()
        {
            RectTransform rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _backgroundImage = gameObject.AddComponent<Image>();
            _backgroundImage.color = PanelColor;
            _backgroundImage.raycastTarget = true; // swallow clicks meant for the board underneath

            VerticalLayoutGroup vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 20f;
            vlg.padding = new RectOffset(40, 40, 40, 40);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            CreateTitle();
            _practiceButton = CreateButton("Practice vs Bots", OnPracticeClicked);
            _createButton = CreateButton("Create Room", OnCreateClicked);
            _modePickerRow = CreateModePickerRow();
            _modePickerRow.SetActive(false);
            _countPickerRow = CreateCountPickerRow();
            _countPickerRow.SetActive(false);
            _joinButton = CreateButton("Join Room", OnJoinClicked);
            _joinInputRow = CreateJoinInputRow();
            _joinInputRow.SetActive(false);
            _codeDisplay = CreateCodeDisplay();
            _codeDisplay.gameObject.SetActive(false);
            _statusText = CreateStatusLabel();
        }

        private void CreateTitle()
        {
            GameObject go = new("Title", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 100f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = TitleFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = BodyTextColor;
            tmp.text = "Pose: Caribbean Dominoes";
        }

        private GameObject CreateButton(string label, Action onClick)
        {
            GameObject go = new($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;

            Image bg = go.AddComponent<Image>();
            bg.color = ButtonColor;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            // Label child centred inside the button
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
            tmp.text = label;
            tmp.raycastTarget = false;

            return go;
        }

        private GameObject CreateJoinInputRow()
        {
            GameObject row = new("JoinInputRow", typeof(RectTransform));
            row.transform.SetParent(transform, worldPositionStays: false);

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredWidth = ButtonWidth;
            rowLayout.preferredHeight = ButtonHeight;

            // Input field
            GameObject inputGo = new("CodeInput", typeof(RectTransform));
            inputGo.transform.SetParent(row.transform, worldPositionStays: false);
            LayoutElement inputLe = inputGo.AddComponent<LayoutElement>();
            inputLe.preferredWidth = 240f;
            inputLe.preferredHeight = ButtonHeight;
            Image inputBg = inputGo.AddComponent<Image>();
            inputBg.color = InputBgColor;
            _codeInput = inputGo.AddComponent<TMP_InputField>();
            _codeInput.targetGraphic = inputBg;
            _codeInput.characterLimit = 6;
            _codeInput.contentType = TMP_InputField.ContentType.Alphanumeric;

            // Input field needs a Text child for display + a Placeholder
            GameObject textArea = new("TextArea", typeof(RectTransform));
            textArea.transform.SetParent(inputGo.transform, worldPositionStays: false);
            RectTransform taRt = (RectTransform)textArea.transform;
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(12f, 4f);
            taRt.offsetMax = new Vector2(-12f, -4f);
            RectMask2D mask = textArea.AddComponent<RectMask2D>();

            GameObject textGo = new("Text", typeof(RectTransform));
            textGo.transform.SetParent(textArea.transform, worldPositionStays: false);
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            TextMeshProUGUI textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;
            textTmp.fontSize = ButtonFontSize;
            textTmp.color = BodyTextColor;
            textTmp.text = string.Empty;

            GameObject placeholderGo = new("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(textArea.transform, worldPositionStays: false);
            RectTransform phRt = (RectTransform)placeholderGo.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            TextMeshProUGUI phTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            phTmp.fontSize = ButtonFontSize;
            phTmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.4f);
            phTmp.text = "Room code";

            _codeInput.textViewport = taRt;
            _codeInput.textComponent = textTmp;
            _codeInput.placeholder = phTmp;

            // Submit button next to the input
            GameObject submit = CreateButton("Join", OnSubmitJoinClicked);
            submit.transform.SetParent(row.transform, worldPositionStays: false);
            LayoutElement submitLe = submit.GetComponent<LayoutElement>();
            submitLe.preferredWidth = 140f;

            return row;
        }

        private GameObject CreateCountPickerRow()
        {
            GameObject row = new("CountPickerRow", typeof(RectTransform));
            row.transform.SetParent(transform, worldPositionStays: false);

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredWidth = ButtonWidth;
            rowLayout.preferredHeight = ButtonHeight;

            for (int count = 2; count <= 4; count++)
            {
                int chosen = count; // capture per-iteration for the closure
                GameObject btn = CreateButton($"{count}P", () => StartCreate(chosen, GameMode.CutThroat));
                btn.transform.SetParent(row.transform, worldPositionStays: false);
                btn.GetComponent<LayoutElement>().preferredWidth = 120f;
            }

            return row;
        }

        private GameObject CreateModePickerRow()
        {
            GameObject row = new("ModePickerRow", typeof(RectTransform));
            row.transform.SetParent(transform, worldPositionStays: false);

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredWidth = ButtonWidth;
            rowLayout.preferredHeight = ButtonHeight;

            GameObject cutThroat = CreateButton("Cut-Throat", OnCutThroatModeClicked);
            cutThroat.transform.SetParent(row.transform, worldPositionStays: false);
            cutThroat.GetComponent<LayoutElement>().preferredWidth = 194f;

            GameObject partner = CreateButton("Partner (4)", OnPartnerModeClicked);
            partner.transform.SetParent(row.transform, worldPositionStays: false);
            partner.GetComponent<LayoutElement>().preferredWidth = 194f;

            return row;
        }

        private TextMeshProUGUI CreateCodeDisplay()
        {
            GameObject go = new("CodeDisplay", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = 120f;

            Image bg = go.AddComponent<Image>();
            bg.color = CardColor;

            GameObject labelGo = new("Code", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = CodeFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = CodeTextColor;
            tmp.characterSpacing = 12f;
            tmp.text = string.Empty;
            tmp.raycastTarget = false;
            return tmp;
        }

        private TextMeshProUGUI CreateStatusLabel()
        {
            GameObject go = new("Status", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 800f;
            le.preferredHeight = 48f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = BodyFontSize;
            tmp.color = BodyTextColor;
            tmp.text = string.Empty;
            return tmp;
        }

        // ---- Button handlers ----------------------------------------------

        private void OnPracticeClicked()
        {
            if (_busy)
            {
                return;
            }
            PracticeChosen?.Invoke();
        }

        private void OnCreateClicked()
        {
            if (_busy)
            {
                return;
            }
            // First click reveals the mode picker (Cut-Throat / Partner). Picking
            // Cut-Throat then reveals the 2/3/4 count picker; picking Partner
            // starts a 4-player game straight away.
            bool show = !_modePickerRow!.activeSelf;
            _modePickerRow.SetActive(show);
            if (!show)
            {
                _countPickerRow!.SetActive(false);
            }
        }

        private void OnCutThroatModeClicked()
        {
            if (_busy)
            {
                return;
            }
            // Cut-Throat supports 2–4 players — reveal the count picker.
            _countPickerRow!.SetActive(true);
        }

        private void OnPartnerModeClicked()
        {
            if (_busy)
            {
                return;
            }
            // Jamaican Partner is always 4 players — no count to pick.
            _countPickerRow!.SetActive(false);
            StartCreate(NetworkedMatch.MaxPlayers, GameMode.Partner);
        }

        private async void StartCreate(int playerCount, GameMode mode)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetActionButtonsVisible(false);

            string code = RoomCodeGenerator.Generate();
            _codeDisplay!.gameObject.SetActive(true);
            _codeDisplay.text = code;
            _statusText!.text = "Creating room…";
            _statusText.color = BodyTextColor;

            EnsurePhotonBootstrap();
            bool ok = await PhotonBootstrap.Instance!.CreateRoom(code, playerCount);
            if (ok)
            {
                _statusText.text = $"Room {code} — waiting for players…";
                OnlineRoomActive?.Invoke(code, playerCount, mode);
            }
            else
            {
                _statusText.text = $"Failed to create room: {PhotonBootstrap.Instance.ErrorMessage}";
                _statusText.color = StatusErrorColor;
                _busy = false;
                SetActionButtonsVisible(true);
                _codeDisplay.gameObject.SetActive(false);
            }
        }

        private void OnJoinClicked()
        {
            if (_busy)
            {
                return;
            }
            // First click: reveal the input row so the player can type a code.
            // The Join row's own "Join" sub-button calls OnSubmitJoinClicked.
            if (!_joinInputRow!.activeSelf)
            {
                _joinInputRow.SetActive(true);
                _codeInput!.Select();
                return;
            }
            // Second click on the outer Join falls through to submit.
            OnSubmitJoinClicked();
        }

        private async void OnSubmitJoinClicked()
        {
            if (_busy)
            {
                return;
            }

            string code = (_codeInput?.text ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length != 6)
            {
                _statusText!.text = "Enter a 6-character room code.";
                _statusText.color = StatusErrorColor;
                return;
            }

            _busy = true;
            SetActionButtonsVisible(false);
            _statusText!.text = $"Joining {code}…";
            _statusText.color = BodyTextColor;

            EnsurePhotonBootstrap();
            bool ok = await PhotonBootstrap.Instance!.JoinRoom(code);
            if (ok)
            {
                _codeDisplay!.gameObject.SetActive(true);
                _codeDisplay.text = code;
                _statusText.text = $"Connected to room {code}.";
                // Count and mode come from the host over the network.
                OnlineRoomActive?.Invoke(code, 0, GameMode.CutThroat);
            }
            else
            {
                _statusText.text = $"Failed to join: {PhotonBootstrap.Instance.ErrorMessage}";
                _statusText.color = StatusErrorColor;
                _busy = false;
                SetActionButtonsVisible(true);
            }
        }

        private void SetActionButtonsVisible(bool visible)
        {
            _practiceButton!.SetActive(visible);
            _createButton!.SetActive(visible);
            _modePickerRow!.SetActive(visible && _modePickerRow.activeSelf);
            _countPickerRow!.SetActive(visible && _countPickerRow.activeSelf);
            _joinButton!.SetActive(visible);
            _joinInputRow!.SetActive(visible && _joinInputRow.activeSelf);
        }

        /// <summary>
        /// Updates the lobby's status line while the room fills. Called by
        /// <see cref="BoardBootstrap"/> as players join (e.g. "3 of 4 joined…").
        /// </summary>
        public void SetWaitingStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.text = text;
                _statusText.color = BodyTextColor;
            }
        }

        private static void EnsurePhotonBootstrap()
        {
            if (PhotonBootstrap.Instance != null)
            {
                return;
            }
            GameObject go = new("PhotonBootstrap");
            go.AddComponent<PhotonBootstrap>();
        }
    }
}
