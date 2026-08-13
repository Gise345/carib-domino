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
    /// The home shell ("Yard") — a cinematic app frame around the lobby, Ludo-style:
    /// a header (profile, coins, settings), a sub-header (Leaderboard / Ranking), a
    /// left ads rail, a bottom tab bar (Shop · Friends · Yard · Profile · Settings),
    /// and a content area. The Yard tab shows a country selector + a horizontal swipe
    /// row of game-mode blocks, each opening its own cinematic screen; the other tabs
    /// are placeholders wired to real data as their features land. Bubbles
    /// <see cref="PracticeChosen"/>, <see cref="OnlineRoomActive"/> and
    /// <see cref="WaitingCancelled"/> for <see cref="BoardBootstrap"/>.
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
        private static readonly Color HeaderColor = new(0.03f, 0.18f, 0.11f, 0.96f);
        private static readonly Color NavColor = new(0.03f, 0.16f, 0.10f, 0.98f);
        private static readonly Color SelectedTint = Color.white;
        private static readonly Color UnselectedTint = new(0.5f, 0.5f, 0.5f, 1f);

        private const float ButtonFontSize = 28f;
        private const float ButtonWidth = 440f;
        private const float ButtonHeight = 84f;
        private const float HeaderHeight = 130f;
        private const float CountryBarHeight = 84f;
        private const float SubHeaderHeight = 116f;
        private const float NavHeight = 130f;

        private const string BuildStamp = "build shell · big bars";

        public event Action? PracticeChosen;
        public event Action<string, int, GameMode, MatchFormat>? OnlineRoomActive;
        public event Action? WaitingCancelled;

        private enum Tab { Yard, Friends, Profile, Settings, Shop }

        private readonly struct Country
        {
            public readonly string Name;
            public readonly bool Live;
            public readonly Color[] Colors;
            public Country(string name, bool live, Color[] colors)
            {
                Name = name;
                Live = live;
                Colors = colors;
            }
        }

        private Country[] _countries = Array.Empty<Country>();
        private int _selectedCountry;

        // Content-area panels (one visible at a time).
        private GameObject? _yardPanel;
        private GameObject? _friendsPanel;
        private GameObject? _profilePanel;
        private GameObject? _settingsPanel;
        private GameObject? _shopPanel;
        private GameObject? _cutThroatScreen;
        private GameObject? _partnerScreen;
        private GameObject? _friendsRoomScreen;
        private GameObject? _comingSoonScreen;
        private GameObject? _rulesScreen;
        private GameObject? _countryPopup;

        private Transform _contentArea = null!;
        private Image? _comingSoonHeader;
        private TextMeshProUGUI? _comingSoonTitle;
        private TextMeshProUGUI? _countryLabel;

        // Bottom-nav tabs, for highlighting.
        private readonly List<(GameObject go, Tab tab)> _navButtons = new();

        // Waiting overlay.
        private GameObject? _waitingOverlay;
        private TextMeshProUGUI? _waitingStatus;

        // Online selection state.
        private MatchFormat _selectedFormat = MatchFormat.ClassicSixLove;
        private int _selectedSize = 2;
        private GameMode _createMode = GameMode.CutThroat;

        private readonly List<(GameObject go, MatchFormat fmt)> _formatButtons = new();
        private readonly List<(GameObject go, int size)> _sizeButtons = new();
        private readonly List<(GameObject go, GameMode mode)> _createModeButtons = new();
        private GameObject? _friendsFormatRow;
        private GameObject? _friendsSizeRow;
        private TMP_InputField? _codeInput;

        private bool _busy;
        private Image? _backgroundImage;

        private void Awake()
        {
            _countries = new[]
            {
                new Country("Jamaica", true, new[] { Hex("#FED100"), Hex("#009B3A"), Hex("#05351C") }),
                new Country("Cuba", false, new[] { Hex("#0A2A8F"), Hex("#CF142B"), Hex("#160308") }),
                new Country("Mexico", false, new[] { Hex("#006847"), Hex("#CE1126"), Hex("#160308") }),
                new Country("Dominican Rep.", false, new[] { Hex("#002D62"), Hex("#CE1126"), Hex("#0A0410") }),
            };
            BuildLayout();
        }

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

        // ---- Build ---------------------------------------------------------

        private void BuildLayout()
        {
            StretchFull((RectTransform)transform);

            _backgroundImage = gameObject.AddComponent<Image>();
            _backgroundImage.color = PanelColor;
            _backgroundImage.raycastTarget = true;

            GameObject scrim = CreateChild(transform, "Scrim");
            StretchFull((RectTransform)scrim.transform);
            Image scrimImg = scrim.AddComponent<Image>();
            scrimImg.sprite = GradientSprite.Vertical(
                new Color(0f, 0f, 0f, 0.5f), new Color(0f, 0f, 0f, 0.2f), new Color(0f, 0f, 0f, 0.55f));
            scrimImg.color = Color.white;
            scrimImg.raycastTarget = false;

            // Shell content (in the content area) + chrome first, then the
            // full-screen overlays on top of them, then popups on top of all.
            BuildContentArea();
            BuildYard();
            _friendsPanel = BuildPlaceholderPanel("Friends", "Connect with friends to play and send coins.\nFacebook & in-game friends — coming soon.");
            _profilePanel = BuildProfilePanel();
            _settingsPanel = BuildSettingsPanel();
            _shopPanel = BuildPlaceholderPanel("Shop", "Buy coins and skins here — coming soon.");

            BuildHeader();
            BuildCountryBar();
            BuildSubHeader();
            BuildSideRail();
            BuildBottomNav();

            _cutThroatScreen = BuildCutThroatScreen();
            _partnerScreen = BuildPartnerScreen();
            _friendsRoomScreen = BuildFriendsRoomScreen();
            _comingSoonScreen = BuildComingSoon();
            _rulesScreen = BuildRulesScreen();

            _countryPopup = BuildCountryPopup();
            _waitingOverlay = BuildWaitingOverlay();

            RefreshFormatButtons();
            RefreshSizeButtons();
            RefreshCreateModeButtons();
            ShowTab(Tab.Yard, _yardPanel);
        }

        private void BuildContentArea()
        {
            GameObject content = CreateChild(transform, "Content");
            RectTransform rt = (RectTransform)content.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, NavHeight);
            rt.offsetMax = new Vector2(0f, -(HeaderHeight + CountryBarHeight + SubHeaderHeight));
            _contentArea = content.transform;
        }

        // ---- Header --------------------------------------------------------

        private void BuildHeader()
        {
            GameObject header = CreateChild(transform, "Header");
            RectTransform rt = (RectTransform)header.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -HeaderHeight);
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, HeaderHeight);
            Image bg = header.AddComponent<Image>();
            bg.color = HeaderColor;

            // Profile picture (left) → Profile tab.
            GameObject pic = CreateChild(header.transform, "ProfilePic");
            RectTransform picRt = (RectTransform)pic.transform;
            picRt.anchorMin = new Vector2(0f, 0.5f);
            picRt.anchorMax = new Vector2(0f, 0.5f);
            picRt.pivot = new Vector2(0f, 0.5f);
            picRt.anchoredPosition = new Vector2(20f, 0f);
            picRt.sizeDelta = new Vector2(84f, 84f);
            Image picBg = pic.AddComponent<Image>();
            picBg.sprite = GradientSprite.RoundedDiagonal(0.5f, Hex("#FED100"), Hex("#009B3A"));
            picBg.color = Color.white;
            Button picBtn = pic.AddComponent<Button>();
            picBtn.targetGraphic = picBg;
            picBtn.onClick.AddListener(() => ShowTab(Tab.Profile, _profilePanel));
            AddIcon(pic.transform, IconFactory.Person(), 52f, ButtonTextColor);

            // Coin value (center-left).
            GameObject coin = CreateChild(header.transform, "Coins");
            RectTransform coinRt = (RectTransform)coin.transform;
            coinRt.anchorMin = new Vector2(0.5f, 0.5f);
            coinRt.anchorMax = new Vector2(0.5f, 0.5f);
            coinRt.pivot = new Vector2(0.5f, 0.5f);
            coinRt.anchoredPosition = new Vector2(0f, 0f);
            coinRt.sizeDelta = new Vector2(360f, 64f);
            Image coinBg = coin.AddComponent<Image>();
            coinBg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0f, 0f, 0f, 0.4f), new Color(0f, 0f, 0f, 0.25f));
            coinBg.color = Color.white;
            AddIconAt(coin.transform, IconFactory.Coin(), 40f, Hex("#FFD24A"), new Vector2(28f, 0f), TextAnchor.MiddleLeft);
            AddLabel(coin.transform, "10,000", 34f, CodeTextColor, TextAlignmentOptions.Center);

            // Gear (right) → Settings tab.
            GameObject gear = CreateChild(header.transform, "Gear");
            RectTransform gearRt = (RectTransform)gear.transform;
            gearRt.anchorMin = new Vector2(1f, 0.5f);
            gearRt.anchorMax = new Vector2(1f, 0.5f);
            gearRt.pivot = new Vector2(1f, 0.5f);
            gearRt.anchoredPosition = new Vector2(-20f, 0f);
            gearRt.sizeDelta = new Vector2(76f, 76f);
            Image gearBg = gear.AddComponent<Image>();
            gearBg.sprite = GradientSprite.RoundedDiagonal(0.4f, new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.08f));
            gearBg.color = Color.white;
            Button gearBtn = gear.AddComponent<Button>();
            gearBtn.targetGraphic = gearBg;
            gearBtn.onClick.AddListener(() => ShowTab(Tab.Settings, _settingsPanel));
            AddIcon(gear.transform, IconFactory.Gear(), 46f, BodyTextColor);

            GameObject stamp = CreateChild(header.transform, "BuildStamp");
            RectTransform stampRt = (RectTransform)stamp.transform;
            stampRt.anchorMin = new Vector2(0f, 0f);
            stampRt.anchorMax = new Vector2(1f, 0f);
            stampRt.pivot = new Vector2(0.5f, 0f);
            stampRt.anchoredPosition = new Vector2(0f, 2f);
            stampRt.sizeDelta = new Vector2(-20f, 18f);
            TextMeshProUGUI stampTmp = stamp.AddComponent<TextMeshProUGUI>();
            stampTmp.alignment = TextAlignmentOptions.Center;
            stampTmp.fontSize = 13f;
            stampTmp.color = new Color(1f, 1f, 1f, 0.45f);
            stampTmp.text = BuildStamp;
            stampTmp.raycastTarget = false;
        }

        private void BuildCountryBar()
        {
            GameObject bar = CreateChild(transform, "CountryBar");
            RectTransform rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(HeaderHeight + 8f));
            rt.sizeDelta = new Vector2(460f, CountryBarHeight - 16f);

            Image selBg = bar.AddComponent<Image>();
            selBg.sprite = GradientSprite.RoundedDiagonal(0.4f, Hex("#FED100"), Hex("#009B3A"));
            selBg.color = Color.white;
            AddShadow(bar, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -3f));
            Button selBtn = bar.AddComponent<Button>();
            selBtn.targetGraphic = selBg;
            selBtn.onClick.AddListener(ToggleCountryPopup);
            _countryLabel = AddLabel(bar.transform, "Jamaica", 34f, Color.white, TextAlignmentOptions.Center);
            AddIconAt(bar.transform, IconFactory.Chevron(down: true), 28f, Color.white, new Vector2(24f, 0f), TextAnchor.MiddleRight);
        }

        private void BuildSubHeader()
        {
            GameObject bar = CreateChild(transform, "SubHeader");
            RectTransform rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -(HeaderHeight + CountryBarHeight + SubHeaderHeight));
            rt.offsetMax = new Vector2(0f, -(HeaderHeight + CountryBarHeight));
            rt.sizeDelta = new Vector2(0f, SubHeaderHeight);

            HorizontalLayoutGroup hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 20f;
            hlg.padding = new RectOffset(0, 0, 8, 8);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            CreatePill(bar.transform, IconFactory.Trophy(), "Leaderboard", Hex("#FFB300"), Hex("#E65100"),
                () => ShowOverlay(_comingSoonScreenForTitle("Leaderboard")));
            CreatePill(bar.transform, IconFactory.Chart(), "Ranking", Hex("#3E8BFF"), Hex("#0B3F9E"),
                () => ShowOverlay(_comingSoonScreenForTitle("Ranking")));
        }

        private void BuildSideRail()
        {
            GameObject rail = CreateChild(transform, "AdsRail");
            RectTransform rt = (RectTransform)rail.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(16f, -20f);
            rt.sizeDelta = new Vector2(84f, 84f);
            Image bg = rail.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.35f, Hex("#F7B500"), Hex("#B26A00"));
            bg.color = Color.white;
            AddShadow(rail, new Color(0f, 0f, 0f, 0.5f), new Vector2(0f, -4f));
            Button btn = rail.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => ShowOverlay(_comingSoonScreenForTitle("Free Coins")));
            AddIcon(rail.transform, IconFactory.Film(), 44f, Color.white);
        }

        // ---- Bottom nav ----------------------------------------------------

        private void BuildBottomNav()
        {
            GameObject nav = CreateChild(transform, "BottomNav");
            RectTransform rt = (RectTransform)nav.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, NavHeight);
            rt.sizeDelta = new Vector2(0f, NavHeight);
            Image bg = nav.AddComponent<Image>();
            bg.color = NavColor;

            HorizontalLayoutGroup hlg = nav.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(8, 8, 8, 8);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;

            CreateNavTab(nav.transform, IconFactory.Bag(), "Shop", Tab.Shop, () => ShowTab(Tab.Shop, _shopPanel), false);
            CreateNavTab(nav.transform, IconFactory.People(), "Friends", Tab.Friends, () => ShowTab(Tab.Friends, _friendsPanel), false);
            CreateNavTab(nav.transform, IconFactory.House(), "YARD", Tab.Yard, () => ShowTab(Tab.Yard, _yardPanel), true);
            CreateNavTab(nav.transform, IconFactory.Person(), "Profile", Tab.Profile, () => ShowTab(Tab.Profile, _profilePanel), false);
            CreateNavTab(nav.transform, IconFactory.Gear(), "Settings", Tab.Settings, () => ShowTab(Tab.Settings, _settingsPanel), false);
        }

        private void CreateNavTab(Transform parent, Sprite icon, string label, Tab tab, Action onClick, bool raised)
        {
            GameObject go = CreateChild(parent, $"Tab_{label}");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = raised ? NavHeight + 12f : NavHeight - 16f;
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.28f,
                raised ? Hex("#FED100") : new Color(1f, 1f, 1f, 0f),
                raised ? Hex("#009B3A") : new Color(1f, 1f, 1f, 0f));
            bg.color = raised ? Color.white : new Color(1f, 1f, 1f, 0f);
            bg.raycastTarget = true;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            GameObject stack = CreateChild(go.transform, "Stack");
            StretchFull((RectTransform)stack.transform);
            VerticalLayoutGroup vlg = stack.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 0f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            AddIconRow(stack.transform, icon, raised ? 52f : 44f, raised ? ButtonTextColor : BodyTextColor);
            AddLabelRow(stack.transform, label, 16f, raised ? ButtonTextColor : new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.8f));

            _navButtons.Add((go, tab));
        }

        private void RefreshNav(Tab active)
        {
            foreach ((GameObject go, Tab tab) in _navButtons)
            {
                Image? img = go.GetComponent<Image>();
                if (img == null)
                {
                    continue;
                }
                bool isYard = tab == Tab.Yard;
                if (isYard)
                {
                    continue; // the raised Yard tab keeps its gradient
                }
                img.color = tab == active ? new Color(1f, 1f, 1f, 0.14f) : new Color(1f, 1f, 1f, 0f);
            }
        }

        // ---- Yard (country selector + horizontal mode row) ----------------

        private void BuildYard()
        {
            _yardPanel = CreateContentPanel("YardPanel");

            // Horizontal scrolling mode row (centre of the Yard). The country
            // selector lives in the shell bar above the Leaderboard / Ranking.
            GameObject scroll = CreateChild(_yardPanel.transform, "ModeScroll");
            RectTransform scrollRt = (RectTransform)scroll.transform;
            scrollRt.anchorMin = new Vector2(0f, 0.5f);
            scrollRt.anchorMax = new Vector2(1f, 0.5f);
            scrollRt.pivot = new Vector2(0.5f, 0.5f);
            scrollRt.anchoredPosition = new Vector2(0f, -10f);
            scrollRt.sizeDelta = new Vector2(-40f, 360f);
            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;

            GameObject viewport = CreateChild(scroll.transform, "Viewport");
            StretchFull((RectTransform)viewport.transform);
            Image vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            viewport.AddComponent<RectMask2D>();
            sr.viewport = (RectTransform)viewport.transform;

            GameObject row = CreateChild(viewport.transform, "Row");
            RectTransform rowRt = (RectTransform)row.transform;
            rowRt.anchorMin = new Vector2(0f, 0.5f);
            rowRt.anchorMax = new Vector2(0f, 0.5f);
            rowRt.pivot = new Vector2(0f, 0.5f);
            HorizontalLayoutGroup rowHlg = row.AddComponent<HorizontalLayoutGroup>();
            rowHlg.childAlignment = TextAnchor.MiddleLeft;
            rowHlg.spacing = 24f;
            rowHlg.padding = new RectOffset(24, 24, 0, 0);
            rowHlg.childControlWidth = true;
            rowHlg.childControlHeight = true;
            rowHlg.childForceExpandWidth = false;
            rowHlg.childForceExpandHeight = false;
            ContentSizeFitter fit = row.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = rowRt;

            CreateModeBlock(row.transform, "Cut Throat\nOnline", "Ranked · 2-4",
                new[] { Hex("#FED100"), Hex("#009B3A"), Hex("#05351C") }, () => ShowOverlay(_cutThroatScreen));
            CreateModeBlock(row.transform, "Partner", "2 v 2 teams",
                new[] { Hex("#00A651"), Hex("#0B3D1E"), Hex("#04120A") }, () => ShowOverlay(_partnerScreen));
            CreateModeBlock(row.transform, "One-Love\nWith Friends", "Private room",
                new[] { Hex("#F7B500"), Hex("#B26A00"), Hex("#3A2200") }, () => ShowOverlay(_friendsRoomScreen));
            CreateModeBlock(row.transform, "Practice", "vs Bots · free",
                new[] { Hex("#4A5568"), Hex("#2D3748"), Hex("#12161F") }, OnPracticeClicked);
        }

        private void OnPracticeClicked()
        {
            if (!_busy)
            {
                PracticeChosen?.Invoke();
            }
        }

        private void CreateModeBlock(Transform parent, string name, string tag, Color[] colors, Action onClick)
        {
            GameObject card = CreateChild(parent, $"Mode_{name}");
            LayoutElement le = card.AddComponent<LayoutElement>();
            le.preferredWidth = 300f;
            le.preferredHeight = 340f;
            Image bg = card.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.14f, colors);
            bg.color = Color.white;
            AddShadow(card, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -6f));
            Button btn = card.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            GameObject foot = CreateChild(card.transform, "Foot");
            RectTransform footRt = (RectTransform)foot.transform;
            footRt.anchorMin = new Vector2(0f, 0f);
            footRt.anchorMax = new Vector2(1f, 0.6f);
            footRt.offsetMin = Vector2.zero;
            footRt.offsetMax = Vector2.zero;
            Image footImg = foot.AddComponent<Image>();
            footImg.sprite = GradientSprite.Vertical(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.72f));
            footImg.raycastTarget = false;

            GameObject nameGo = CreateChild(card.transform, "Name");
            RectTransform nameRt = (RectTransform)nameGo.transform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.anchoredPosition = new Vector2(0f, 58f);
            nameRt.sizeDelta = new Vector2(-24f, 80f);
            TextMeshProUGUI nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.alignment = TextAlignmentOptions.BottomLeft;
            nameTmp.fontSize = 32f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = Color.white;
            nameTmp.text = name;
            nameTmp.raycastTarget = false;
            nameTmp.margin = new Vector4(18f, 0f, 10f, 0f);

            GameObject tagGo = CreateChild(card.transform, "Tag");
            RectTransform tagRt = (RectTransform)tagGo.transform;
            tagRt.anchorMin = new Vector2(0f, 0f);
            tagRt.anchorMax = new Vector2(1f, 0f);
            tagRt.pivot = new Vector2(0.5f, 0f);
            tagRt.anchoredPosition = new Vector2(0f, 24f);
            tagRt.sizeDelta = new Vector2(-24f, 28f);
            TextMeshProUGUI tagTmp = tagGo.AddComponent<TextMeshProUGUI>();
            tagTmp.alignment = TextAlignmentOptions.BottomLeft;
            tagTmp.fontSize = 18f;
            tagTmp.color = CodeTextColor;
            tagTmp.text = tag;
            tagTmp.raycastTarget = false;
            tagTmp.margin = new Vector4(18f, 0f, 10f, 0f);
        }

        // ---- Country popup -------------------------------------------------

        private GameObject BuildCountryPopup()
        {
            GameObject overlay = CreateChild(transform, "CountryPopup");
            StretchFull((RectTransform)overlay.transform);
            Image dim = overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            Button dismiss = overlay.AddComponent<Button>();
            dismiss.targetGraphic = dim;
            dismiss.onClick.AddListener(() => overlay.SetActive(false));

            GameObject list = CreateChild(overlay.transform, "List");
            RectTransform rt = (RectTransform)list.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(460f, 460f);
            VerticalLayoutGroup vlg = list.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = 14f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            for (int i = 0; i < _countries.Length; i++)
            {
                int idx = i;
                Country c = _countries[i];
                GameObject b = CreateButton(c.Live ? c.Name : $"{c.Name}  (soon)", () => OnCountryPicked(idx));
                b.transform.SetParent(list.transform, worldPositionStays: false);
                if (!c.Live)
                {
                    Tint(b, false);
                }
            }

            overlay.SetActive(false);
            return overlay;
        }

        private void ToggleCountryPopup()
        {
            if (_countryPopup != null)
            {
                _countryPopup.SetActive(!_countryPopup.activeSelf);
            }
        }

        private void OnCountryPicked(int index)
        {
            _countryPopup?.SetActive(false);
            if (_countries[index].Live)
            {
                _selectedCountry = index;
                if (_countryLabel != null)
                {
                    _countryLabel.text = _countries[index].Name;
                }
            }
            else
            {
                ShowOverlay(_comingSoonScreenForCountry(index));
            }
        }

        // ---- Mode screens --------------------------------------------------

        private GameObject BuildCutThroatScreen()
        {
            (GameObject screen, RectTransform body) = CreateFullScreen("CutThroatScreen", "Cut Throat Online");
            FillStack(body);

            CreateSectionLabel(body, "GAME FORMAT");
            GameObject fmtRow = CreateRow(body);
            AddFormatButton(fmtRow.transform, "Classic 6 Love", MatchFormat.ClassicSixLove);
            AddFormatButton(fmtRow.transform, "Quick Love", MatchFormat.QuickLove);

            CreateSectionLabel(body, "PLAYERS");
            GameObject sizeRow = CreateRow(body);
            for (int n = 2; n <= 4; n++)
            {
                AddSizeButton(sizeRow.transform, n);
            }

            CreateRewardsCard(body, "Winner takes all", "Winner takes the pot + 2,000 key bonus");
            CreateSpacer(body);
            CreateEntryRow(body);
            CreateBigButton(body, "Start", () => StartOnline(GameMode.CutThroat, _selectedSize, _selectedFormat));
            return screen;
        }

        private GameObject BuildPartnerScreen()
        {
            (GameObject screen, RectTransform body) = CreateFullScreen("PartnerScreen", "Partner");
            FillStack(body);

            CreateSectionLabel(body, "RANDOM 2 v 2 · 4 PLAYERS");
            CreatePartnerPieces(body);
            CreateRewardsCard(body, "Winning team takes all", "The pot + key bonus, split with your partner");
            CreateSpacer(body);
            CreateEntryRow(body);
            CreateBigButton(body, "Find Match",
                () => StartOnline(GameMode.Partner, NetworkedMatch.MaxPlayers, MatchFormat.ClassicSixLove));
            return screen;
        }

        private GameObject BuildFriendsRoomScreen()
        {
            (GameObject screen, RectTransform body) = CreateFullScreen("FriendsRoomScreen", "One-Love With Friends");
            FillStack(body);

            CreateSectionLabel(body, "CREATE A ROOM");
            GameObject modeRow = CreateRow(body);
            AddCreateModeButton(modeRow.transform, "Cut-Throat", GameMode.CutThroat);
            AddCreateModeButton(modeRow.transform, "Partner", GameMode.Partner);

            _friendsFormatRow = CreateRow(body);
            AddFormatButton(_friendsFormatRow.transform, "Classic 6 Love", MatchFormat.ClassicSixLove);
            AddFormatButton(_friendsFormatRow.transform, "Quick Love", MatchFormat.QuickLove);

            _friendsSizeRow = CreateRow(body);
            for (int n = 2; n <= 4; n++)
            {
                AddSizeButton(_friendsSizeRow.transform, n);
            }

            CreateRewardsCard(body, "Winner takes all", "Beat your friends to win big!");
            CreateSpacer(body);
            CreateEntryRow(body);
            CreateBigButton(body, "Create", OnCreateRoomClicked);

            CreateSectionLabel(body, "HAVE A TABLE CODE?");
            GameObject joinRow = CreateJoinRow(body);
            joinRow.transform.SetParent(body, worldPositionStays: false);
            return screen;
        }

        private void CreateSpacer(Transform parent)
        {
            GameObject go = CreateChild(parent, "Spacer");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.preferredHeight = 8f;
        }

        private void CreateEntryRow(Transform parent)
        {
            GameObject go = CreateChild(parent, "Entry");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 320f;
            le.preferredHeight = 56f;
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0f, 0f, 0f, 0.4f), new Color(0f, 0f, 0f, 0.25f));
            bg.color = Color.white;
            AddIconAt(go.transform, IconFactory.Coin(), 32f, Hex("#FFD24A"), new Vector2(20f, 0f), TextAnchor.MiddleLeft);
            AddLabel(go.transform, "Entry: 1,000", 28f, CodeTextColor, TextAlignmentOptions.Center);
        }

        private void CreateBigButton(Transform parent, string label, Action onClick)
        {
            GameObject go = CreateButton(label, onClick);
            go.transform.SetParent(parent, worldPositionStays: false);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth + 60f;
            le.preferredHeight = 116f;
            TextMeshProUGUI? tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.fontSize = 40f;
            }
            Image? img = go.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = GradientSprite.RoundedDiagonal(0.28f, Hex("#4CD964"), Hex("#1FA845"));
                img.color = Color.white;
            }
        }

        private void CreatePartnerPieces(Transform parent)
        {
            GameObject row = CreateChild(parent, "Pieces");
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = 90f;
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 20f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            string[] colors = { "#3B7DFF", "#FF4D4D", "#3BD16F", "#FFD23B" };
            foreach (string c in colors)
            {
                GameObject piece = CreateChild(row.transform, "Piece");
                LayoutElement ple = piece.AddComponent<LayoutElement>();
                ple.preferredWidth = 70f;
                ple.preferredHeight = 80f;
                Image img = piece.AddComponent<Image>();
                img.sprite = GradientSprite.RoundedDiagonal(0.5f, Hex(c), new Color(0f, 0f, 0f, 0.4f));
                img.color = Color.white;
                img.raycastTarget = false;
            }
        }

        private void OnCreateRoomClicked()
        {
            if (_busy)
            {
                return;
            }
            if (_createMode == GameMode.Partner)
            {
                StartCreate(NetworkedMatch.MaxPlayers, GameMode.Partner, MatchFormat.ClassicSixLove);
            }
            else
            {
                StartCreate(_selectedSize, GameMode.CutThroat, _selectedFormat);
            }
        }

        // ---- Placeholder tab panels ---------------------------------------

        private GameObject BuildPlaceholderPanel(string title, string body)
        {
            GameObject screen = CreateContentPanel($"{title}Panel");
            CreateTitle(screen.transform, title, -30f, 56f);
            GameObject b = CreateChild(screen.transform, "Body");
            RectTransform rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(720f, 200f);
            TextMeshProUGUI tmp = b.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 26f;
            tmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.9f);
            tmp.text = body;
            tmp.raycastTarget = false;
            return screen;
        }

        private GameObject BuildProfilePanel()
        {
            GameObject screen = CreateContentPanel("ProfilePanel");
            CreateTitle(screen.transform, "Profile", -30f, 56f);
            GameObject col = CreateColumn(screen.transform);
            StatRow(col.transform, "Coins", "10,000");
            StatRow(col.transform, "Games played", "0");
            StatRow(col.transform, "Wins", "0");
            StatRow(col.transform, "Win rate", "—");
            CreateSectionLabel(col.transform, "ACHIEVEMENTS — COMING SOON");
            return screen;
        }

        private GameObject BuildSettingsPanel()
        {
            GameObject screen = CreateContentPanel("SettingsPanel");
            CreateTitle(screen.transform, "Settings", -30f, 56f);
            GameObject col = CreateColumn(screen.transform);
            StatRow(col.transform, "Sound", "On");
            StatRow(col.transform, "Music", "On");
            GameObject rules = CreateButton("How to Play", () => ShowOverlay(_rulesScreen));
            rules.transform.SetParent(col.transform, worldPositionStays: false);
            return screen;
        }

        private GameObject BuildRulesScreen()
        {
            (GameObject screen, RectTransform body) = CreateFullScreen("RulesScreen", "How to Play");
            GameObject b = CreateChild(body, "Body");
            StretchFull((RectTransform)b.transform);
            ((RectTransform)b.transform).offsetMin = new Vector2(10f, 10f);
            ((RectTransform)b.transform).offsetMax = new Vector2(-10f, -10f);
            TextMeshProUGUI tmp = b.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.fontSize = 24f;
            tmp.color = BodyTextColor;
            tmp.raycastTarget = false;
            tmp.text =
                "<b>Cut-Throat</b> — every player for themselves. Win a round to score a game (1000 pts). "
                + "First to <b>6 Love (6000)</b> in Classic, or <b>3000</b> in Quick Love, wins the match.\n\n"
                + "<b>Pose</b> — the double-six leads the first round; the previous winner leads after.\n\n"
                + "<b>Blocked</b> — if no one can play, the lowest pip-count wins the round.\n\n"
                + "<b>Battle</b> — when two players tie on games won, they go for it: double-six poses until "
                + "one wins, and the loser drops back to <b>LOVE</b> (0). That's cut-throat.\n\n"
                + "<b>Key</b> — win on your last tile with both ends locked and no one else holding those "
                + "numbers: +2000, mash up the board.\n\n"
                + "<b>Partner</b> — 2 v 2, partners across the table.";
            return screen;
        }

        private void StatRow(Transform parent, string label, string value)
        {
            GameObject row = CreateChild(parent, $"Stat_{label}");
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = 60f;
            Image bg = row.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.2f, new Color(0f, 0f, 0f, 0.35f), new Color(0f, 0f, 0f, 0.22f));
            bg.color = Color.white;
            bg.raycastTarget = false;

            GameObject l = CreateChild(row.transform, "L");
            RectTransform lrt = (RectTransform)l.transform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0.6f, 1f);
            lrt.offsetMin = new Vector2(18f, 0f);
            lrt.offsetMax = Vector2.zero;
            TextMeshProUGUI lt = l.AddComponent<TextMeshProUGUI>();
            lt.alignment = TextAlignmentOptions.MidlineLeft;
            lt.fontSize = 24f;
            lt.color = BodyTextColor;
            lt.text = label;
            lt.raycastTarget = false;

            GameObject v = CreateChild(row.transform, "V");
            RectTransform vrt = (RectTransform)v.transform;
            vrt.anchorMin = new Vector2(0.6f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = new Vector2(-18f, 0f);
            TextMeshProUGUI vt = v.AddComponent<TextMeshProUGUI>();
            vt.alignment = TextAlignmentOptions.MidlineRight;
            vt.fontSize = 24f;
            vt.fontStyle = FontStyles.Bold;
            vt.color = CodeTextColor;
            vt.text = value;
            vt.raycastTarget = false;
        }

        // ---- Coming soon ---------------------------------------------------

        private GameObject BuildComingSoon()
        {
            GameObject screen = CreateChild(transform, "ComingSoonScreen");
            StretchFull((RectTransform)screen.transform);
            Image sbg = screen.AddComponent<Image>();
            sbg.sprite = GradientSprite.Vertical(Hex("#0A3D22"), Hex("#062A17"), Hex("#04160C"));
            sbg.color = Color.white;
            CreateBackButton(screen.transform, HideOverlays);

            GameObject banner = CreateChild(screen.transform, "Banner");
            RectTransform bRt = (RectTransform)banner.transform;
            bRt.anchorMin = new Vector2(0.5f, 0.5f);
            bRt.anchorMax = new Vector2(0.5f, 0.5f);
            bRt.pivot = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = new Vector2(0f, 60f);
            bRt.sizeDelta = new Vector2(600f, 220f);
            _comingSoonHeader = banner.AddComponent<Image>();
            _comingSoonHeader.sprite = GradientSprite.RoundedDiagonal(0.1f, Hex("#FED100"), Hex("#009B3A"));
            _comingSoonHeader.color = Color.white;
            AddShadow(banner, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -6f));
            _comingSoonTitle = AddLabel(banner.transform, "Coming soon", 52f, Color.white, TextAlignmentOptions.Center);

            GameObject body = CreateChild(screen.transform, "Body");
            RectTransform bodyRt = (RectTransform)body.transform;
            bodyRt.anchorMin = new Vector2(0.5f, 0.5f);
            bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRt.pivot = new Vector2(0.5f, 0.5f);
            bodyRt.anchoredPosition = new Vector2(0f, -120f);
            bodyRt.sizeDelta = new Vector2(700f, 100f);
            TextMeshProUGUI bodyTmp = body.AddComponent<TextMeshProUGUI>();
            bodyTmp.alignment = TextAlignmentOptions.Top;
            bodyTmp.fontSize = 28f;
            bodyTmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.9f);
            bodyTmp.text = "On its way — check back soon.";
            bodyTmp.raycastTarget = false;

            screen.SetActive(false);
            _overlays.Add(screen);
            return screen;
        }

        private GameObject? _comingSoonScreenForTitle(string title)
        {
            if (_comingSoonTitle != null)
            {
                _comingSoonTitle.text = title;
            }
            if (_comingSoonHeader != null)
            {
                _comingSoonHeader.sprite = GradientSprite.RoundedDiagonal(0.1f, Hex("#FED100"), Hex("#009B3A"));
            }
            return _comingSoonScreen;
        }

        private GameObject? _comingSoonScreenForCountry(int index)
        {
            Country c = _countries[index];
            if (_comingSoonTitle != null)
            {
                _comingSoonTitle.text = c.Name;
            }
            if (_comingSoonHeader != null)
            {
                _comingSoonHeader.sprite = GradientSprite.RoundedDiagonal(0.1f, c.Colors);
            }
            return _comingSoonScreen;
        }

        // ---- Navigation ----------------------------------------------------

        private void ShowTab(Tab tab, GameObject? panel)
        {
            // Yard tab always returns to the Yard home.
            ShowContent(tab == Tab.Yard ? _yardPanel : panel, tab);
        }

        private void ShowContent(GameObject? panel, Tab activeTab)
        {
            foreach (GameObject? p in AllContentPanels())
            {
                p?.SetActive(p == panel);
            }
            RefreshNav(activeTab);
        }

        private IEnumerable<GameObject?> AllContentPanels()
        {
            yield return _yardPanel;
            yield return _friendsPanel;
            yield return _profilePanel;
            yield return _settingsPanel;
            yield return _shopPanel;
        }

        // Full-screen mode/info screens (over the whole shell) — Ludo-style.
        private readonly List<GameObject> _overlays = new();

        private void ShowOverlay(GameObject? overlay)
        {
            foreach (GameObject o in _overlays)
            {
                o.SetActive(o == overlay);
            }
        }

        private void HideOverlays()
        {
            foreach (GameObject o in _overlays)
            {
                o.SetActive(false);
            }
        }

        // A full-screen game-mode screen: opaque felt background, a coloured banner
        // header (back + title), and a body region below it for the caller's content.
        private (GameObject screen, RectTransform body) CreateFullScreen(string name, string title)
        {
            GameObject screen = CreateChild(transform, name);
            StretchFull((RectTransform)screen.transform);
            Image bg = screen.AddComponent<Image>();
            bg.sprite = GradientSprite.Vertical(Hex("#0A3D22"), Hex("#062A17"), Hex("#04160C"));
            bg.color = Color.white;
            bg.raycastTarget = true;

            GameObject banner = CreateChild(screen.transform, "Banner");
            RectTransform bRt = (RectTransform)banner.transform;
            bRt.anchorMin = new Vector2(0f, 1f);
            bRt.anchorMax = new Vector2(1f, 1f);
            bRt.pivot = new Vector2(0.5f, 1f);
            bRt.offsetMin = new Vector2(0f, -150f);
            bRt.offsetMax = Vector2.zero;
            bRt.sizeDelta = new Vector2(0f, 150f);
            Image bImg = banner.AddComponent<Image>();
            bImg.sprite = GradientSprite.Vertical(Hex("#FED100"), Hex("#009B3A"));
            bImg.color = Color.white;
            AddShadow(banner, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -4f));

            AddLabel(banner.transform, title, 46f, Color.white, TextAlignmentOptions.Center);
            CreateBackButton(banner.transform, HideOverlays);

            GameObject body = CreateChild(screen.transform, "Body");
            RectTransform bodyRt = (RectTransform)body.transform;
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.offsetMin = new Vector2(30f, 30f);
            bodyRt.offsetMax = new Vector2(-30f, -170f);

            screen.SetActive(false);
            _overlays.Add(screen);
            return (screen, bodyRt);
        }

        // A vertical stack that fills the body with generous spacing (fills the
        // screen rather than clumping in the middle).
        private VerticalLayoutGroup FillStack(RectTransform body)
        {
            VerticalLayoutGroup vlg = body.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 26f;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            return vlg;
        }

        // ---- Matchmaking (preserved) --------------------------------------

        private async void StartOnline(GameMode mode, int size, MatchFormat format)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            EnterWaitingState(mode == GameMode.Partner ? "Finding players for 2v2…" : "Finding players…");
            EnsurePhotonBootstrap();
            bool ok = await PhotonBootstrap.Instance!.QuickMatch(mode, size, format);
            if (ok)
            {
                OnlineRoomActive?.Invoke(PhotonBootstrap.Instance.CurrentRoomCode ?? string.Empty, size, mode, format);
            }
            else
            {
                FailWaiting($"Failed to find a match: {PhotonBootstrap.Instance.ErrorMessage}");
            }
        }

        private async void StartCreate(int playerCount, GameMode mode, MatchFormat format)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            string code = RoomCodeGenerator.Generate();
            EnterWaitingState($"Room {code} — creating…");
            EnsurePhotonBootstrap();
            bool ok = await PhotonBootstrap.Instance!.CreateRoom(code, playerCount);
            if (ok)
            {
                SetWaitingStatus($"Room {code} — waiting for players…");
                OnlineRoomActive?.Invoke(code, playerCount, mode, format);
            }
            else
            {
                FailWaiting($"Failed to create room: {PhotonBootstrap.Instance.ErrorMessage}");
            }
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
                EnterWaitingState("Enter a 6-character room code.");
                _busy = false;
                return;
            }
            _busy = true;
            EnterWaitingState($"Joining {code}…");
            EnsurePhotonBootstrap();
            bool ok = await PhotonBootstrap.Instance!.JoinRoom(code);
            if (ok)
            {
                SetWaitingStatus($"Connected to room {code}.");
                OnlineRoomActive?.Invoke(code, 0, GameMode.CutThroat, MatchFormat.ClassicSixLove);
            }
            else
            {
                FailWaiting($"Failed to join: {PhotonBootstrap.Instance.ErrorMessage}");
            }
        }

        private void OnCancelClicked()
        {
            if (_busy)
            {
                WaitingCancelled?.Invoke();
                _busy = false;
            }
            _waitingOverlay!.SetActive(false);
        }

        public void SetWaitingStatus(string text)
        {
            if (_waitingStatus != null)
            {
                _waitingStatus.text = text;
                _waitingStatus.color = BodyTextColor;
            }
        }

        private void EnterWaitingState(string status)
        {
            _waitingOverlay!.SetActive(true);
            SetWaitingStatus(status);
        }

        private void FailWaiting(string error)
        {
            _busy = false;
            if (_waitingStatus != null)
            {
                _waitingStatus.text = error;
                _waitingStatus.color = StatusErrorColor;
            }
        }

        // ---- Selection toggles --------------------------------------------

        private void AddFormatButton(Transform parent, string label, MatchFormat fmt)
        {
            GameObject go = CreateButton(label, () => { _selectedFormat = fmt; RefreshFormatButtons(); });
            go.transform.SetParent(parent, worldPositionStays: false);
            go.GetComponent<LayoutElement>().preferredWidth = 210f;
            _formatButtons.Add((go, fmt));
        }

        private void AddSizeButton(Transform parent, int size)
        {
            GameObject go = CreateButton($"{size}P", () => { _selectedSize = size; RefreshSizeButtons(); });
            go.transform.SetParent(parent, worldPositionStays: false);
            go.GetComponent<LayoutElement>().preferredWidth = 120f;
            _sizeButtons.Add((go, size));
        }

        private void AddCreateModeButton(Transform parent, string label, GameMode mode)
        {
            GameObject go = CreateButton(label, () =>
            {
                _createMode = mode;
                RefreshCreateModeButtons();
                bool cutThroat = mode == GameMode.CutThroat;
                _friendsFormatRow?.SetActive(cutThroat);
                _friendsSizeRow?.SetActive(cutThroat);
            });
            go.transform.SetParent(parent, worldPositionStays: false);
            go.GetComponent<LayoutElement>().preferredWidth = 210f;
            _createModeButtons.Add((go, mode));
        }

        private void RefreshFormatButtons()
        {
            foreach ((GameObject go, MatchFormat fmt) in _formatButtons)
            {
                Tint(go, fmt == _selectedFormat);
            }
        }

        private void RefreshSizeButtons()
        {
            foreach ((GameObject go, int size) in _sizeButtons)
            {
                Tint(go, size == _selectedSize);
            }
        }

        private void RefreshCreateModeButtons()
        {
            foreach ((GameObject go, GameMode mode) in _createModeButtons)
            {
                Tint(go, mode == _createMode);
            }
        }

        private static void Tint(GameObject go, bool selected)
        {
            Image? img = go.GetComponent<Image>();
            if (img != null)
            {
                img.color = selected ? SelectedTint : UnselectedTint;
            }
        }

        // ---- Waiting overlay ----------------------------------------------

        private GameObject BuildWaitingOverlay()
        {
            GameObject overlay = CreateChild(transform, "WaitingOverlay");
            StretchFull((RectTransform)overlay.transform);
            Image dim = overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.82f);
            dim.raycastTarget = true;

            GameObject status = CreateChild(overlay.transform, "Status");
            RectTransform sRt = (RectTransform)status.transform;
            sRt.anchorMin = new Vector2(0.5f, 0.5f);
            sRt.anchorMax = new Vector2(0.5f, 0.5f);
            sRt.pivot = new Vector2(0.5f, 0.5f);
            sRt.anchoredPosition = new Vector2(0f, 60f);
            sRt.sizeDelta = new Vector2(820f, 120f);
            _waitingStatus = status.AddComponent<TextMeshProUGUI>();
            _waitingStatus.alignment = TextAlignmentOptions.Center;
            _waitingStatus.fontSize = 34f;
            _waitingStatus.color = BodyTextColor;
            _waitingStatus.raycastTarget = false;

            GameObject cancel = CreateButton("Cancel", OnCancelClicked);
            cancel.transform.SetParent(overlay.transform, worldPositionStays: false);
            RectTransform cRt = (RectTransform)cancel.transform;
            cRt.anchorMin = new Vector2(0.5f, 0.5f);
            cRt.anchorMax = new Vector2(0.5f, 0.5f);
            cRt.pivot = new Vector2(0.5f, 0.5f);
            cRt.anchoredPosition = new Vector2(0f, -80f);
            cRt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

            overlay.SetActive(false);
            return overlay;
        }

        // ---- Reusable builders --------------------------------------------

        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private GameObject CreateContentPanel(string name)
        {
            GameObject panel = CreateChild(_contentArea, name);
            StretchFull((RectTransform)panel.transform);
            return panel;
        }

        private GameObject CreateColumn(Transform parent)
        {
            GameObject col = CreateChild(parent, "Column");
            RectTransform rt = (RectTransform)col.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -20f);
            rt.sizeDelta = new Vector2(ButtonWidth + 60f, 700f);
            VerticalLayoutGroup vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 14f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            return col;
        }

        private GameObject CreateRow(Transform parent)
        {
            GameObject row = CreateChild(parent, "Row");
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;
            return row;
        }

        private void CreateSectionLabel(Transform parent, string text)
        {
            GameObject go = CreateChild(parent, "Section");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = 30f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 20f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.8f);
            tmp.characterSpacing = 6f;
            tmp.text = text;
            tmp.raycastTarget = false;
        }

        private void CreateRewardsCard(Transform parent, string headline, string subline)
        {
            GameObject card = CreateChild(parent, "Rewards");
            LayoutElement le = card.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth + 60f;
            le.preferredHeight = 210f;
            Image bg = card.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.12f, new Color(0.10f, 0.42f, 0.26f), new Color(0.03f, 0.16f, 0.10f));
            bg.color = Color.white;
            AddShadow(card, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -4f));

            GameObject text = CreateChild(card.transform, "Text");
            StretchFull((RectTransform)text.transform);
            ((RectTransform)text.transform).offsetMin = new Vector2(20f, 16f);
            ((RectTransform)text.transform).offsetMax = new Vector2(-20f, -16f);
            TextMeshProUGUI tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 26f;
            tmp.color = BodyTextColor;
            tmp.text = $"<size=34><b><color=#FFD54A>Rewards</color></b></size>\n\n<size=40><b>{headline}</b></size>\n<size=24>{subline}</size>";
            tmp.raycastTarget = false;
        }

        private GameObject CreateJoinRow(Transform parent)
        {
            GameObject row = CreateRow(parent);
            GameObject inputGo = CreateChild(row.transform, "CodeInput");
            LayoutElement inputLe = inputGo.AddComponent<LayoutElement>();
            inputLe.preferredWidth = 280f;
            inputLe.preferredHeight = ButtonHeight;
            Image inputBg = inputGo.AddComponent<Image>();
            inputBg.color = InputBgColor;
            _codeInput = inputGo.AddComponent<TMP_InputField>();
            _codeInput.targetGraphic = inputBg;
            _codeInput.characterLimit = 6;
            _codeInput.contentType = TMP_InputField.ContentType.Alphanumeric;

            GameObject textArea = CreateChild(inputGo.transform, "TextArea");
            RectTransform taRt = (RectTransform)textArea.transform;
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(12f, 4f);
            taRt.offsetMax = new Vector2(-12f, -4f);
            textArea.AddComponent<RectMask2D>();

            GameObject textGo = CreateChild(textArea.transform, "Text");
            StretchFull((RectTransform)textGo.transform);
            TextMeshProUGUI textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;
            textTmp.fontSize = ButtonFontSize;
            textTmp.color = BodyTextColor;

            GameObject ph = CreateChild(textArea.transform, "Placeholder");
            StretchFull((RectTransform)ph.transform);
            TextMeshProUGUI phTmp = ph.AddComponent<TextMeshProUGUI>();
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            phTmp.fontSize = ButtonFontSize;
            phTmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.4f);
            phTmp.text = "Room code";

            _codeInput.textViewport = taRt;
            _codeInput.textComponent = textTmp;
            _codeInput.placeholder = phTmp;

            GameObject join = CreateButton("Join", OnSubmitJoinClicked);
            join.transform.SetParent(row.transform, worldPositionStays: false);
            join.GetComponent<LayoutElement>().preferredWidth = 148f;
            return row;
        }

        private GameObject CreateButton(string label, Action onClick)
        {
            GameObject go = CreateChild(transform, $"Btn_{label}");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = ButtonHeight;
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.22f, new Color(0.99f, 0.97f, 0.90f), new Color(0.93f, 0.89f, 0.78f));
            bg.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());
            AddShadow(go, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -3f));
            AddLabel(go.transform, label, ButtonFontSize, ButtonTextColor, TextAlignmentOptions.Center);
            return go;
        }

        private void CreatePill(Transform parent, Sprite icon, string label, Color c1, Color c2, Action onClick)
        {
            GameObject go = CreateChild(parent, $"Pill_{label}");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 320f;
            le.preferredHeight = 92f;
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.32f, c1, c2);
            bg.color = Color.white;
            AddShadow(go, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -4f));
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());
            AddIconAt(go.transform, icon, 46f, Color.white, new Vector2(26f, 0f), TextAnchor.MiddleLeft);
            GameObject lbl = CreateChild(go.transform, "Label");
            RectTransform lrt = (RectTransform)lbl.transform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(64f, 0f);
            lrt.offsetMax = Vector2.zero;
            TextMeshProUGUI tmp = lbl.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 28f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.text = label;
            tmp.raycastTarget = false;
        }

        private void CreateBackButton(Transform parent, Action onClick)
        {
            GameObject go = CreateChild(parent, "BackButton");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f, -12f);
            rt.sizeDelta = new Vector2(120f, 60f);
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.4f, new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.08f));
            bg.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());
            AddIconAt(go.transform, IconFactory.Chevron(down: false), 26f, BodyTextColor, new Vector2(20f, 0f), TextAnchor.MiddleLeft);
            AddLabel(go.transform, "Back", 26f, BodyTextColor, TextAlignmentOptions.Center);
        }

        private void CreateTitle(Transform parent, string text, float y, float size)
        {
            GameObject go = CreateChild(parent, "Title");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(1000f, 100f);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = BodyTextColor;
            tmp.characterSpacing = 4f;
            tmp.text = text;
            tmp.raycastTarget = false;
            AddShadow(go, new Color(0f, 0f, 0f, 0.75f), new Vector2(2f, -2f));
        }

        private TextMeshProUGUI AddLabel(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            GameObject go = CreateChild(parent, "Label");
            StretchFull((RectTransform)go.transform);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = align;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            tmp.text = text;
            tmp.raycastTarget = false;
            return tmp;
        }

        private Image AddIcon(Transform parent, Sprite icon, float size, Color color)
        {
            GameObject go = CreateChild(parent, "Icon");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            Image img = go.AddComponent<Image>();
            img.sprite = icon;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        private void AddIconAt(Transform parent, Sprite icon, float size, Color color, Vector2 edgeOffset, TextAnchor anchor)
        {
            Vector2 a = anchor switch
            {
                TextAnchor.MiddleLeft => new Vector2(0f, 0.5f),
                TextAnchor.MiddleRight => new Vector2(1f, 0.5f),
                _ => new Vector2(0.5f, 0.5f),
            };
            GameObject go = CreateChild(parent, "Icon");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.pivot = a;
            float x = anchor == TextAnchor.MiddleRight ? -edgeOffset.x : edgeOffset.x;
            rt.anchoredPosition = new Vector2(x, edgeOffset.y);
            rt.sizeDelta = new Vector2(size, size);
            Image img = go.AddComponent<Image>();
            img.sprite = icon;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = true;
        }

        private void AddIconRow(Transform parent, Sprite icon, float size, Color color)
        {
            GameObject row = CreateChild(parent, "IconRow");
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = size + 6f;
            GameObject ic = CreateChild(row.transform, "Icon");
            RectTransform rt = (RectTransform)ic.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            Image img = ic.AddComponent<Image>();
            img.sprite = icon;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = true;
        }

        private void AddLabelRow(Transform parent, string text, float size, Color color)
        {
            GameObject go = CreateChild(parent, "Row");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = size + 8f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.text = text;
            tmp.raycastTarget = false;
        }

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

        private static Color Hex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

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
