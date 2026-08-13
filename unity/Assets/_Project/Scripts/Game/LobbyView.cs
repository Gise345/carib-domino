#nullable enable
using System;
using System.Collections.Generic;
using Pose.Core;
using Pose.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// Cinematic front-of-house for the app. Three stacked full-screen screens
    /// over a shared background photo + darkening scrim:
    /// <list type="bullet">
    ///   <item><b>Hub</b> — a 2×2 grid of country blocks (Jamaica, Cuba, Mexico,
    ///         Dominican Rep.) each painted with its flag colours. Jamaica is
    ///         live; the rest read "Coming soon".</item>
    ///   <item><b>Jamaica</b> — the live game menu (Practice, Cut Throat Online,
    ///         Create Room, Join Room) with a Back button.</item>
    ///   <item><b>Coming soon</b> — a themed placeholder for the other countries.</item>
    /// </list>
    /// Self-built (no editor wiring). Bubbles <see cref="PracticeChosen"/> and
    /// <see cref="OnlineRoomActive"/> for <see cref="BoardBootstrap"/> to consume.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class LobbyView : MonoBehaviour
    {
        private static readonly Color PanelColor = new(0.05f, 0.30f, 0.18f, 0.97f);
        private static readonly Color ButtonTextColor = new(0.05f, 0.30f, 0.18f);
        private static readonly Color BodyTextColor = new(0.97f, 0.95f, 0.88f);
        private static readonly Color CodeTextColor = new(1.0f, 0.92f, 0.50f);
        private static readonly Color InputBgColor = new(0.04f, 0.20f, 0.12f);
        private static readonly Color StatusErrorColor = new(1.0f, 0.55f, 0.45f);

        private const float TitleFontSize = 72f;
        private const float SubtitleFontSize = 26f;
        private const float ButtonFontSize = 28f;
        private const float BodyFontSize = 24f;
        private const float CodeFontSize = 72f;
        private const float ButtonWidth = 420f;
        private const float ButtonHeight = 80f;

        public event Action? PracticeChosen;

        /// <summary>
        /// Fires with the room code and, for the creator, the chosen player count
        /// (2–4), game mode and Cut-Throat series format. Joiners pass count 0 and
        /// placeholders — the real values arrive from the host over the network.
        /// </summary>
        public event Action<string, int, GameMode, MatchFormat>? OnlineRoomActive;

        /// <summary>Fires when the player backs out of the waiting room before the deal.</summary>
        public event Action? WaitingCancelled;

        // A country block on the hub.
        private readonly struct Country
        {
            public readonly string Name;
            public readonly string Tagline;
            public readonly bool Live;
            public readonly Color[] Colors;

            public Country(string name, string tagline, bool live, Color[] colors)
            {
                Name = name;
                Tagline = tagline;
                Live = live;
                Colors = colors;
            }
        }

        private Country[] _countries = Array.Empty<Country>();

        // Screens.
        private GameObject? _hubScreen;
        private GameObject? _jamaicaScreen;
        private GameObject? _comingSoonScreen;

        // The action list lives under this transform (Jamaica screen).
        private Transform _actionParent = null!;

        // Action buttons and pickers (hidden while connecting).
        private GameObject? _practiceButton;
        private GameObject? _cutThroatOnlineButton;
        private GameObject? _onlineFormatRow;
        private GameObject? _onlineSizePickerRow;
        private GameObject? _partnerOnlineButton;
        private GameObject? _createButton;
        private GameObject? _modePickerRow;
        private GameObject? _createFormatRow;
        private GameObject? _countPickerRow;
        private GameObject? _joinButton;
        private GameObject? _joinInputRow;
        private GameObject? _jamaicaBackButton;
        private GameObject? _cancelButton;

        // Cut-Throat series format, chosen via the format pickers (default Classic).
        private MatchFormat _selectedFormat = MatchFormat.ClassicSixLove;
        private readonly List<(GameObject go, MatchFormat fmt)> _formatButtons = new();

        private TMP_InputField? _codeInput;
        private TextMeshProUGUI? _statusText;
        private TextMeshProUGUI? _codeDisplay;

        // Coming-soon screen, re-themed per country on show.
        private Image? _comingSoonHeader;
        private TextMeshProUGUI? _comingSoonTitle;

        private bool _busy;
        private Image? _backgroundImage;

        private void Awake()
        {
            _countries = new[]
            {
                new Country("Jamaica", "Cut-Throat & Partner", true,
                    new[] { Hex("#FED100"), Hex("#009B3A"), Hex("#05351C") }),
                new Country("Cuba", "Coming soon", false,
                    new[] { Hex("#0A2A8F"), Hex("#CF142B"), Hex("#160308") }),
                new Country("Mexico", "Coming soon", false,
                    new[] { Hex("#006847"), Hex("#CE1126"), Hex("#160308") }),
                new Country("Dominican Rep.", "Coming soon", false,
                    new[] { Hex("#002D62"), Hex("#CE1126"), Hex("#0A0410") }),
            };

            BuildLayout();
        }

        /// <summary>
        /// Applies the shared background photo behind every screen. Null restores
        /// the flat felt-green fill.
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
            StretchFull((RectTransform)transform);

            _backgroundImage = gameObject.AddComponent<Image>();
            _backgroundImage.color = PanelColor;
            _backgroundImage.raycastTarget = true; // swallow clicks meant for the board underneath

            // Cinematic scrim: darker at the top and bottom (letterbox feel),
            // lighter through the middle, so text and blocks lift off the photo.
            GameObject scrim = new("Scrim", typeof(RectTransform));
            scrim.transform.SetParent(transform, worldPositionStays: false);
            StretchFull((RectTransform)scrim.transform);
            Image scrimImg = scrim.AddComponent<Image>();
            scrimImg.sprite = GradientSprite.Vertical(
                new Color(0f, 0f, 0f, 0.78f),
                new Color(0f, 0f, 0f, 0.35f),
                new Color(0f, 0f, 0f, 0.86f));
            scrimImg.color = Color.white;
            scrimImg.raycastTarget = false;

            _hubScreen = BuildHub();
            _jamaicaScreen = BuildJamaica();
            _comingSoonScreen = BuildComingSoon();

            ShowHub();
        }

        // ---- Hub screen ----------------------------------------------------

        // Bump this every build. It renders in the lobby corner so we can confirm
        // the running binary matches the source (rules out a stale ScriptAssemblies
        // cache when a change "doesn't show up").
        private const string BuildStamp = "build dfe72f6 · Classic 6 Love / Quick Love";

        private GameObject BuildHub()
        {
            GameObject screen = CreateScreen("HubScreen");

            GameObject stamp = new("BuildStamp", typeof(RectTransform));
            stamp.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform stampRt = (RectTransform)stamp.transform;
            stampRt.anchorMin = new Vector2(0f, 0f);
            stampRt.anchorMax = new Vector2(1f, 0f);
            stampRt.pivot = new Vector2(0.5f, 0f);
            stampRt.anchoredPosition = new Vector2(0f, 8f);
            stampRt.sizeDelta = new Vector2(-20f, 24f);
            TextMeshProUGUI stampTmp = stamp.AddComponent<TextMeshProUGUI>();
            stampTmp.alignment = TextAlignmentOptions.Center;
            stampTmp.fontSize = 16f;
            stampTmp.color = new Color(1f, 1f, 1f, 0.5f);
            stampTmp.text = BuildStamp;
            stampTmp.raycastTarget = false;

            GameObject title = new("Title", typeof(RectTransform));
            title.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -60f);
            titleRt.sizeDelta = new Vector2(1000f, 120f);
            TextMeshProUGUI titleTmp = title.AddComponent<TextMeshProUGUI>();
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.fontSize = TitleFontSize;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = BodyTextColor;
            titleTmp.characterSpacing = 6f;
            titleTmp.text = "POSE";
            titleTmp.raycastTarget = false;
            AddShadow(title, new Color(0f, 0f, 0f, 0.75f), new Vector2(3f, -3f));

            GameObject subtitle = new("Subtitle", typeof(RectTransform));
            subtitle.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform subRt = (RectTransform)subtitle.transform;
            subRt.anchorMin = new Vector2(0.5f, 1f);
            subRt.anchorMax = new Vector2(0.5f, 1f);
            subRt.pivot = new Vector2(0.5f, 1f);
            subRt.anchoredPosition = new Vector2(0f, -168f);
            subRt.sizeDelta = new Vector2(1000f, 40f);
            TextMeshProUGUI subTmp = subtitle.AddComponent<TextMeshProUGUI>();
            subTmp.alignment = TextAlignmentOptions.Center;
            subTmp.fontSize = SubtitleFontSize;
            subTmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.85f);
            subTmp.characterSpacing = 10f;
            subTmp.text = "CHOOSE YOUR TABLE";
            subTmp.raycastTarget = false;

            // Centered 2×2 grid of country blocks.
            GameObject grid = new("CountryGrid", typeof(RectTransform));
            grid.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform gridRt = (RectTransform)grid.transform;
            gridRt.anchorMin = new Vector2(0.5f, 0.5f);
            gridRt.anchorMax = new Vector2(0.5f, 0.5f);
            gridRt.pivot = new Vector2(0.5f, 0.5f);
            gridRt.anchoredPosition = new Vector2(0f, -30f);
            GridLayoutGroup glg = grid.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(300f, 300f);
            glg.spacing = new Vector2(28f, 28f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.MiddleCenter;
            ContentSizeFitter fitter = grid.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            for (int i = 0; i < _countries.Length; i++)
            {
                CreateCountryCard(i, _countries[i], grid.transform);
            }

            return screen;
        }

        private void CreateCountryCard(int index, Country country, Transform parent)
        {
            GameObject card = new($"Card_{country.Name}", typeof(RectTransform));
            card.transform.SetParent(parent, worldPositionStays: false);

            Image bg = card.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.14f, country.Colors);
            bg.color = country.Live ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.9f);
            bg.type = Image.Type.Simple;

            AddShadow(card, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -6f));

            Button btn = card.AddComponent<Button>();
            btn.targetGraphic = bg;
            int captured = index;
            btn.onClick.AddListener(() => OnCountryClicked(captured));

            // Bottom scrim so the name reads over the bright gradient.
            GameObject foot = new("Foot", typeof(RectTransform));
            foot.transform.SetParent(card.transform, worldPositionStays: false);
            RectTransform footRt = (RectTransform)foot.transform;
            footRt.anchorMin = new Vector2(0f, 0f);
            footRt.anchorMax = new Vector2(1f, 0.55f);
            footRt.offsetMin = Vector2.zero;
            footRt.offsetMax = Vector2.zero;
            Image footImg = foot.AddComponent<Image>();
            footImg.sprite = GradientSprite.Vertical(
                new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.72f));
            footImg.raycastTarget = false;

            // Country name (bottom-left).
            GameObject name = new("Name", typeof(RectTransform));
            name.transform.SetParent(card.transform, worldPositionStays: false);
            RectTransform nameRt = (RectTransform)name.transform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.anchoredPosition = new Vector2(0f, 52f);
            nameRt.sizeDelta = new Vector2(-32f, 48f);
            TextMeshProUGUI nameTmp = name.AddComponent<TextMeshProUGUI>();
            nameTmp.alignment = TextAlignmentOptions.BottomLeft;
            nameTmp.fontSize = 34f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = Color.white;
            nameTmp.text = country.Name;
            nameTmp.raycastTarget = false;
            nameTmp.margin = new Vector4(20f, 0f, 12f, 0f);
            AddShadow(name, new Color(0f, 0f, 0f, 0.8f), new Vector2(2f, -2f));

            // Tagline / status.
            GameObject tag = new("Tag", typeof(RectTransform));
            tag.transform.SetParent(card.transform, worldPositionStays: false);
            RectTransform tagRt = (RectTransform)tag.transform;
            tagRt.anchorMin = new Vector2(0f, 0f);
            tagRt.anchorMax = new Vector2(1f, 0f);
            tagRt.pivot = new Vector2(0.5f, 0f);
            tagRt.anchoredPosition = new Vector2(0f, 22f);
            tagRt.sizeDelta = new Vector2(-32f, 28f);
            TextMeshProUGUI tagTmp = tag.AddComponent<TextMeshProUGUI>();
            tagTmp.alignment = TextAlignmentOptions.BottomLeft;
            tagTmp.fontSize = 18f;
            tagTmp.fontStyle = country.Live ? FontStyles.Bold : FontStyles.Normal;
            tagTmp.color = country.Live ? CodeTextColor : new Color(1f, 1f, 1f, 0.75f);
            tagTmp.text = country.Live ? "▶  PLAY" : "COMING SOON";
            tagTmp.raycastTarget = false;
            tagTmp.margin = new Vector4(20f, 0f, 12f, 0f);
            tagTmp.characterSpacing = country.Live ? 0f : 4f;
        }

        private void OnCountryClicked(int index)
        {
            if (_busy)
            {
                return;
            }
            if (_countries[index].Live)
            {
                ShowJamaica();
            }
            else
            {
                ShowComingSoon(index);
            }
        }

        // ---- Jamaica screen (the live game menu) ---------------------------

        private GameObject BuildJamaica()
        {
            GameObject screen = CreateScreen("JamaicaScreen");

            _jamaicaBackButton = CreateBackButton(screen.transform, ShowHub);

            GameObject header = new("Header", typeof(RectTransform));
            header.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform hRt = (RectTransform)header.transform;
            hRt.anchorMin = new Vector2(0.5f, 1f);
            hRt.anchorMax = new Vector2(0.5f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.anchoredPosition = new Vector2(0f, -70f);
            hRt.sizeDelta = new Vector2(900f, 90f);
            TextMeshProUGUI hTmp = header.AddComponent<TextMeshProUGUI>();
            hTmp.alignment = TextAlignmentOptions.Center;
            hTmp.fontSize = 60f;
            hTmp.fontStyle = FontStyles.Bold;
            hTmp.color = BodyTextColor;
            hTmp.text = "Jamaica";
            hTmp.raycastTarget = false;
            AddShadow(header, new Color(0f, 0f, 0f, 0.75f), new Vector2(2f, -2f));

            // Vertically-stacked action list, centred.
            GameObject content = new("Content", typeof(RectTransform));
            content.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform cRt = (RectTransform)content.transform;
            cRt.anchorMin = new Vector2(0.5f, 0.5f);
            cRt.anchorMax = new Vector2(0.5f, 0.5f);
            cRt.pivot = new Vector2(0.5f, 0.5f);
            cRt.anchoredPosition = new Vector2(0f, -20f);
            cRt.sizeDelta = new Vector2(ButtonWidth + 40f, 640f);
            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 18f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            _actionParent = content.transform;

            _practiceButton = CreateButton("Practice vs Bots", OnPracticeClicked);
            _cutThroatOnlineButton = CreateButton("Cut Throat Online", OnCutThroatOnlineClicked);
            _onlineFormatRow = CreateFormatPickerRow();
            _onlineFormatRow.SetActive(false);
            _onlineSizePickerRow = CreateOnlineSizePickerRow();
            _onlineSizePickerRow.SetActive(false);
            _partnerOnlineButton = CreateButton("Partner Online (2v2)", OnPartnerOnlineClicked);
            _createButton = CreateButton("Create Room", OnCreateClicked);
            _modePickerRow = CreateModePickerRow();
            _modePickerRow.SetActive(false);
            _createFormatRow = CreateFormatPickerRow();
            _createFormatRow.SetActive(false);
            _countPickerRow = CreateCountPickerRow();
            _countPickerRow.SetActive(false);
            RefreshFormatHighlights();
            _joinButton = CreateButton("Join Room", OnJoinClicked);
            _joinInputRow = CreateJoinInputRow();
            _joinInputRow.SetActive(false);
            _codeDisplay = CreateCodeDisplay();
            _codeDisplay.gameObject.SetActive(false);
            _statusText = CreateStatusLabel();
            _cancelButton = CreateButton("Cancel", OnCancelClicked);
            _cancelButton.SetActive(false);

            return screen;
        }

        // ---- Coming-soon screen -------------------------------------------

        private GameObject BuildComingSoon()
        {
            GameObject screen = CreateScreen("ComingSoonScreen");

            CreateBackButton(screen.transform, ShowHub);

            // Themed banner (gradient set per country on show).
            GameObject banner = new("Banner", typeof(RectTransform));
            banner.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform bRt = (RectTransform)banner.transform;
            bRt.anchorMin = new Vector2(0.5f, 0.5f);
            bRt.anchorMax = new Vector2(0.5f, 0.5f);
            bRt.pivot = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = new Vector2(0f, 60f);
            bRt.sizeDelta = new Vector2(560f, 220f);
            _comingSoonHeader = banner.AddComponent<Image>();
            _comingSoonHeader.type = Image.Type.Simple;
            AddShadow(banner, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -6f));

            GameObject nameGo = new("Country", typeof(RectTransform));
            nameGo.transform.SetParent(banner.transform, worldPositionStays: false);
            StretchFull((RectTransform)nameGo.transform);
            _comingSoonTitle = nameGo.AddComponent<TextMeshProUGUI>();
            _comingSoonTitle.alignment = TextAlignmentOptions.Center;
            _comingSoonTitle.fontSize = 56f;
            _comingSoonTitle.fontStyle = FontStyles.Bold;
            _comingSoonTitle.color = Color.white;
            _comingSoonTitle.raycastTarget = false;
            AddShadow(nameGo, new Color(0f, 0f, 0f, 0.8f), new Vector2(2f, -2f));

            GameObject body = new("Body", typeof(RectTransform));
            body.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform bodyRt = (RectTransform)body.transform;
            bodyRt.anchorMin = new Vector2(0.5f, 0.5f);
            bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRt.pivot = new Vector2(0.5f, 0.5f);
            bodyRt.anchoredPosition = new Vector2(0f, -110f);
            bodyRt.sizeDelta = new Vector2(700f, 120f);
            TextMeshProUGUI bodyTmp = body.AddComponent<TextMeshProUGUI>();
            bodyTmp.alignment = TextAlignmentOptions.Top;
            bodyTmp.fontSize = 26f;
            bodyTmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.9f);
            bodyTmp.text = "This table is on its way.\nJamaica is live now — tap Back to play.";
            bodyTmp.raycastTarget = false;

            return screen;
        }

        private void ShowComingSoon(int index)
        {
            Country c = _countries[index];
            if (_comingSoonHeader != null)
            {
                _comingSoonHeader.sprite = GradientSprite.RoundedDiagonal(0.1f, c.Colors);
                _comingSoonHeader.color = Color.white;
            }
            if (_comingSoonTitle != null)
            {
                _comingSoonTitle.text = c.Name;
            }
            SetActiveScreen(_comingSoonScreen);
        }

        // ---- Navigation ----------------------------------------------------

        private void ShowHub() => SetActiveScreen(_hubScreen);

        private void ShowJamaica() => SetActiveScreen(_jamaicaScreen);

        private void SetActiveScreen(GameObject? active)
        {
            _hubScreen?.SetActive(_hubScreen == active);
            _jamaicaScreen?.SetActive(_jamaicaScreen == active);
            _comingSoonScreen?.SetActive(_comingSoonScreen == active);
        }

        // ---- Reusable builders --------------------------------------------

        private GameObject CreateScreen(string name)
        {
            GameObject screen = new(name, typeof(RectTransform));
            screen.transform.SetParent(transform, worldPositionStays: false);
            StretchFull((RectTransform)screen.transform);
            return screen;
        }

        private GameObject CreateBackButton(Transform parent, Action onClick)
        {
            GameObject go = new("BackButton", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(28f, -28f);
            rt.sizeDelta = new Vector2(120f, 64f);

            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.4f,
                new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.08f));
            bg.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            GameObject label = new("Label", typeof(RectTransform));
            label.transform.SetParent(go.transform, worldPositionStays: false);
            StretchFull((RectTransform)label.transform);
            TextMeshProUGUI tmp = label.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 26f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = BodyTextColor;
            tmp.text = "‹ Back";
            tmp.raycastTarget = false;
            return go;
        }

        private GameObject CreateButton(string label, Action onClick)
        {
            GameObject go = new($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(_actionParent, worldPositionStays: false);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;

            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.22f,
                new Color(0.99f, 0.97f, 0.90f), new Color(0.93f, 0.89f, 0.78f));
            bg.color = Color.white;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());
            AddShadow(go, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -3f));

            GameObject labelGo = new("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            StretchFull((RectTransform)labelGo.transform);
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
            row.transform.SetParent(_actionParent, worldPositionStays: false);

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

            GameObject inputGo = new("CodeInput", typeof(RectTransform));
            inputGo.transform.SetParent(row.transform, worldPositionStays: false);
            LayoutElement inputLe = inputGo.AddComponent<LayoutElement>();
            inputLe.preferredWidth = 260f;
            inputLe.preferredHeight = ButtonHeight;
            Image inputBg = inputGo.AddComponent<Image>();
            inputBg.color = InputBgColor;
            _codeInput = inputGo.AddComponent<TMP_InputField>();
            _codeInput.targetGraphic = inputBg;
            _codeInput.characterLimit = 6;
            _codeInput.contentType = TMP_InputField.ContentType.Alphanumeric;

            GameObject textArea = new("TextArea", typeof(RectTransform));
            textArea.transform.SetParent(inputGo.transform, worldPositionStays: false);
            RectTransform taRt = (RectTransform)textArea.transform;
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(12f, 4f);
            taRt.offsetMax = new Vector2(-12f, -4f);
            textArea.AddComponent<RectMask2D>();

            GameObject textGo = new("Text", typeof(RectTransform));
            textGo.transform.SetParent(textArea.transform, worldPositionStays: false);
            StretchFull((RectTransform)textGo.transform);
            TextMeshProUGUI textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;
            textTmp.fontSize = ButtonFontSize;
            textTmp.color = BodyTextColor;
            textTmp.text = string.Empty;

            GameObject placeholderGo = new("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(textArea.transform, worldPositionStays: false);
            StretchFull((RectTransform)placeholderGo.transform);
            TextMeshProUGUI phTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            phTmp.fontSize = ButtonFontSize;
            phTmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.4f);
            phTmp.text = "Room code";

            _codeInput.textViewport = taRt;
            _codeInput.textComponent = textTmp;
            _codeInput.placeholder = phTmp;

            GameObject submit = CreateButton("Join", OnSubmitJoinClicked);
            submit.transform.SetParent(row.transform, worldPositionStays: false);
            submit.GetComponent<LayoutElement>().preferredWidth = 148f;

            return row;
        }

        private GameObject CreateCountPickerRow()
        {
            GameObject row = CreatePickerRow("CountPickerRow");
            for (int count = 2; count <= 4; count++)
            {
                int chosen = count;
                GameObject btn = CreateButton($"{count}P", () => StartCreate(chosen, GameMode.CutThroat, _selectedFormat));
                btn.transform.SetParent(row.transform, worldPositionStays: false);
                btn.GetComponent<LayoutElement>().preferredWidth = 124f;
            }
            return row;
        }

        private GameObject CreateOnlineSizePickerRow()
        {
            GameObject row = CreatePickerRow("OnlineSizePickerRow");
            for (int count = 2; count <= 4; count++)
            {
                int chosen = count;
                GameObject btn = CreateButton($"{count}P", () => StartOnline(GameMode.CutThroat, chosen, _selectedFormat));
                btn.transform.SetParent(row.transform, worldPositionStays: false);
                btn.GetComponent<LayoutElement>().preferredWidth = 124f;
            }
            return row;
        }

        private GameObject CreateFormatPickerRow()
        {
            GameObject row = CreatePickerRow("FormatPickerRow");

            GameObject classic = CreateButton("Classic 6 Love\n6000 to win", () => OnFormatClicked(MatchFormat.ClassicSixLove));
            classic.transform.SetParent(row.transform, worldPositionStays: false);
            classic.GetComponent<LayoutElement>().preferredWidth = 200f;
            _formatButtons.Add((classic, MatchFormat.ClassicSixLove));

            GameObject quick = CreateButton("Quick Love\n3000 to win", () => OnFormatClicked(MatchFormat.QuickLove));
            quick.transform.SetParent(row.transform, worldPositionStays: false);
            quick.GetComponent<LayoutElement>().preferredWidth = 200f;
            _formatButtons.Add((quick, MatchFormat.QuickLove));

            return row;
        }

        private void OnFormatClicked(MatchFormat format)
        {
            if (_busy)
            {
                return;
            }
            _selectedFormat = format;
            RefreshFormatHighlights();
        }

        private void RefreshFormatHighlights()
        {
            foreach ((GameObject go, MatchFormat fmt) in _formatButtons)
            {
                Image? img = go.GetComponent<Image>();
                if (img != null)
                {
                    img.color = fmt == _selectedFormat ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
                }
            }
        }

        private GameObject CreateModePickerRow()
        {
            GameObject row = CreatePickerRow("ModePickerRow");

            GameObject cutThroat = CreateButton("Cut-Throat", OnCutThroatModeClicked);
            cutThroat.transform.SetParent(row.transform, worldPositionStays: false);
            cutThroat.GetComponent<LayoutElement>().preferredWidth = 200f;

            GameObject partner = CreateButton("Partner (4)", OnPartnerModeClicked);
            partner.transform.SetParent(row.transform, worldPositionStays: false);
            partner.GetComponent<LayoutElement>().preferredWidth = 200f;

            return row;
        }

        private GameObject CreatePickerRow(string name)
        {
            GameObject row = new(name, typeof(RectTransform));
            row.transform.SetParent(_actionParent, worldPositionStays: false);

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
            return row;
        }

        private TextMeshProUGUI CreateCodeDisplay()
        {
            GameObject go = new("CodeDisplay", typeof(RectTransform));
            go.transform.SetParent(_actionParent, worldPositionStays: false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = 120f;

            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.2f,
                new Color(0.06f, 0.24f, 0.14f), new Color(0.02f, 0.14f, 0.09f));
            bg.color = Color.white;

            GameObject labelGo = new("Code", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            StretchFull((RectTransform)labelGo.transform);
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
            go.transform.SetParent(_actionParent, worldPositionStays: false);
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

        private void OnCutThroatOnlineClicked()
        {
            if (_busy)
            {
                return;
            }
            bool show = !_onlineSizePickerRow!.activeSelf;
            _onlineFormatRow!.SetActive(show);
            _onlineSizePickerRow.SetActive(show);
            if (show)
            {
                _modePickerRow!.SetActive(false);
                _createFormatRow!.SetActive(false);
                _countPickerRow!.SetActive(false);
            }
        }

        private void OnPartnerOnlineClicked()
        {
            if (_busy)
            {
                return;
            }
            // Partner is a fixed 2-v-2 table — no size to pick, match straight away.
            // Partner has no series format (single round); Classic is a placeholder.
            _onlineFormatRow!.SetActive(false);
            _onlineSizePickerRow!.SetActive(false);
            StartOnline(GameMode.Partner, NetworkedMatch.MaxPlayers, MatchFormat.ClassicSixLove);
        }

        private async void StartOnline(GameMode mode, int size, MatchFormat format)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            EnterWaitingState();

            _statusText!.text = mode == GameMode.Partner
                ? "Finding players for 2v2…"
                : "Finding players…";
            _statusText.color = BodyTextColor;

            EnsurePhotonBootstrap();
            bool ok = await PhotonBootstrap.Instance!.QuickMatch(mode, size, format);
            if (ok)
            {
                OnlineRoomActive?.Invoke(
                    PhotonBootstrap.Instance.CurrentRoomCode ?? string.Empty,
                    size,
                    mode,
                    format);
            }
            else
            {
                _statusText.text = $"Failed to find a match: {PhotonBootstrap.Instance.ErrorMessage}";
                _statusText.color = StatusErrorColor;
                _busy = false;
                SetActionButtonsVisible(true);
            }
        }

        private void OnCreateClicked()
        {
            if (_busy)
            {
                return;
            }
            bool show = !_modePickerRow!.activeSelf;
            _modePickerRow.SetActive(show);
            if (show)
            {
                _onlineSizePickerRow!.SetActive(false);
            }
            else
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
            // Cut-Throat: pick the series format, then the player count.
            _createFormatRow!.SetActive(true);
            _countPickerRow!.SetActive(true);
        }

        private void OnPartnerModeClicked()
        {
            if (_busy)
            {
                return;
            }
            // Jamaican Partner is always 4 players, single round — no count/format.
            _createFormatRow!.SetActive(false);
            _countPickerRow!.SetActive(false);
            StartCreate(NetworkedMatch.MaxPlayers, GameMode.Partner, MatchFormat.ClassicSixLove);
        }

        private async void StartCreate(int playerCount, GameMode mode, MatchFormat format)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            EnterWaitingState();

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
                OnlineRoomActive?.Invoke(code, playerCount, mode, format);
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
            if (!_joinInputRow!.activeSelf)
            {
                _joinInputRow.SetActive(true);
                _codeInput!.Select();
                return;
            }
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
            EnterWaitingState();
            _statusText!.text = $"Joining {code}…";
            _statusText.color = BodyTextColor;

            EnsurePhotonBootstrap();
            bool ok = await PhotonBootstrap.Instance!.JoinRoom(code);
            if (ok)
            {
                _codeDisplay!.gameObject.SetActive(true);
                _codeDisplay.text = code;
                _statusText.text = $"Connected to room {code}.";
                OnlineRoomActive?.Invoke(code, 0, GameMode.CutThroat, MatchFormat.ClassicSixLove);
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
            _cutThroatOnlineButton!.SetActive(visible);
            _onlineFormatRow!.SetActive(visible && _onlineFormatRow.activeSelf);
            _onlineSizePickerRow!.SetActive(visible && _onlineSizePickerRow.activeSelf);
            _partnerOnlineButton!.SetActive(visible);
            _createButton!.SetActive(visible);
            _modePickerRow!.SetActive(visible && _modePickerRow.activeSelf);
            _createFormatRow!.SetActive(visible && _createFormatRow.activeSelf);
            _countPickerRow!.SetActive(visible && _countPickerRow.activeSelf);
            _joinButton!.SetActive(visible);
            _joinInputRow!.SetActive(visible && _joinInputRow.activeSelf);
            // Don't let the player navigate away mid-connect via the country Back.
            _jamaicaBackButton!.SetActive(visible);
            // Restoring the menu also clears any waiting-room Cancel button.
            if (visible && _cancelButton != null)
            {
                _cancelButton.SetActive(false);
            }
        }

        // Cancel matchmaking from the waiting room: drop the session and restore
        // the menu. There IS a way out of the waiting room now.
        private void OnCancelClicked()
        {
            if (!_busy)
            {
                return;
            }
            WaitingCancelled?.Invoke();
            _busy = false;
            _codeDisplay!.gameObject.SetActive(false);
            _statusText!.text = string.Empty;
            SetActionButtonsVisible(true);
        }

        private void EnterWaitingState()
        {
            SetActionButtonsVisible(false);
            _cancelButton!.SetActive(true);
        }

        /// <summary>
        /// Updates the status line while the room fills. Called by
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

        // ---- Small helpers -------------------------------------------------

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddShadow(GameObject go, Color color, Vector2 distance)
        {
            Shadow shadow = go.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
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
