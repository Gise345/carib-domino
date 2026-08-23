#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly Color UnavailableTint = new(0.5f, 0.5f, 0.5f, 1f);

        private const float ButtonFontSize = 28f;
        private const float ButtonWidth = 440f;
        private const float ButtonHeight = 84f;
        private const float HeaderHeight = 236f;
        private const float CountryBarHeight = 88f;
        private const float SubHeaderHeight = 120f;
        private const float NavHeight = 196f;

        /// <summary>Left/right inset of a room's body stack — see <see cref="RoomKit.Screen"/>.</summary>
        private const float RoomBodyInset = 30f;

        /// <summary>
        /// Canvas width the rooms are designed against. Only used before the
        /// canvas has laid out, when the real width is not yet known.
        /// </summary>
        private const float ReferenceScreenWidth = 800f;

        private const string BuildStamp = "build shell · tall header/nav · 3D cards";

        public event Action? PracticeChosen;
        public event Action<string, int, GameMode, MatchFormat>? OnlineRoomActive;
        public event Action? WaitingCancelled;

        /// <summary>Raised when the player logs out from the Profile tab (M7).</summary>
        public event Action? LoggedOut;

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

        // Social (M7 phase 2): leaderboard overlay, friends list, account section.
        private GameObject? _leaderboardScreen;
        private Transform? _leaderboardContent;
        private TextMeshProUGUI? _leaderboardStatus;
        private string _leaderboardScope = "global";
        private Transform? _friendsContent;
        private TextMeshProUGUI? _friendsStatus;
        private GameObject? _friendsActionButton;
        private TextMeshProUGUI? _friendsActionLabel;
        private TextMeshProUGUI? _inviteStatus;
        private TextMeshProUGUI? _accountStatusLabel;
        private TextMeshProUGUI? _accountActionLabel;
        private TextMeshProUGUI? _statCoins;
        private TextMeshProUGUI? _statGames;
        private TextMeshProUGUI? _statWins;
        private TextMeshProUGUI? _statWinRate;
        private Image? _profileAvatar;
        private TextMeshProUGUI? _profileName;
        private TextMeshProUGUI? _profileTag;
        private int _selectedLanguage;
        private Image? _headerAvatar;

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

        // The format is one preference across every room — a player who wants
        // the short series should not have to say so on each screen.
        private readonly List<(GameObject go, MatchFormat fmt)> _formatTiles = new();

        // Table size is per-room: Cut Throat's pick is not the friends room's.
        private readonly List<(GameObject go, int seats)> _cutThroatSeats = new();
        private readonly List<(GameObject go, int seats)> _friendsSeats = new();
        private int _friendsSeatCount = NetworkedMatch.MaxPlayers;

        // A friends room is Cut-Throat or Partner, and the two halves of the
        // screen below the switch are built separately and swapped.
        private readonly List<(GameObject go, GameMode mode)> _friendsModes = new();
        private RectTransform? _friendsCutThroatSection;
        private RectTransform? _friendsPartnerSection;

        // Restated whenever a choice changes — rounds to win, and every number
        // on a rewards board.
        private readonly List<Action> _roomRefreshers = new();

        // Art targets. BoardBootstrap hands the sprites over after these screens
        // are built, so the rooms are assembled art-less and dressed afterwards.
        private readonly List<GameObject> _classicTiles = new();
        private readonly List<GameObject> _quickTiles = new();
        private Image? _cutThroatHero;
        private Image? _partnerHero;
        private Image? _oneLoveHero;
        private Image? _cutThroatBoard;
        private Image? _partnerBoard;
        private Image? _oneLoveBoard;
        private Image? _oneLovePartnerBoard;
        private readonly List<Image> _roomBackgrounds = new();
        private RoomArt _roomArt = new();

        private TMP_InputField? _codeInput;

        private bool _busy;
        private Image? _backgroundImage;
        private Image? _logoImage;
        private Sprite? _logoSprite;
        private Sprite? _modeButtonSprite;
        private readonly List<Image> _modeFrames = new();
        private readonly List<GameObject> _modeProcedural = new();
        private TextMeshProUGUI? _taglineText;
        private TMP_FontAsset? _titleFont;

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

        /// <summary>The Pose logo shown above the Yard's mode list. Applied to the
        /// logo Image built in <see cref="BuildYard"/> (deferred like the background).</summary>
        public void SetLogoSprite(Sprite? sprite)
        {
            _logoSprite = sprite;
            if (_logoImage == null)
            {
                return;
            }
            _logoImage.sprite = sprite;
            _logoImage.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// The wooden button-frame sprite (1560×384, transparent, no text) drawn
        /// behind every mode block. When set, it replaces the procedurally-drawn
        /// wood/teal/rivets; the icon + label stay drawn on top. Null keeps the
        /// drawn fallback. Deferred like the logo/background.
        /// </summary>
        public void SetModeButtonSprite(Sprite? sprite)
        {
            _modeButtonSprite = sprite;
            bool hasImage = sprite != null;
            foreach (Image frame in _modeFrames)
            {
                if (frame != null)
                {
                    ApplyModeFrame(frame, sprite);
                }
            }
            foreach (GameObject proc in _modeProcedural)
            {
                if (proc != null)
                {
                    proc.SetActive(!hasImage);
                }
            }
        }

        /// <summary>
        /// Hands the three game rooms their painted art — titles, format tiles
        /// and the rewards board.
        ///
        /// Deferred like the logo and background: the rooms are built during
        /// Awake and the sprites arrive afterwards from
        /// <see cref="BoardBootstrap"/>, so this dresses screens that already
        /// exist. Any field left null keeps that piece's lettered stand-in,
        /// which is what lets the art land one file at a time.
        /// </summary>
        /// <param name="art">The art bundle; null clears back to the stand-ins.</param>
        public void SetRoomArt(RoomArt? art)
        {
            _roomArt = art ?? new RoomArt();
            ApplyRoomArt();
        }

        private void ApplyRoomArt()
        {
            // Rooms are laid out at the canvas width, inset by the body stack's
            // own padding — the art has to be sized against the space it will
            // actually occupy, not the whole screen.
            float screenWidth = ((RectTransform)transform).rect.width;
            if (screenWidth <= 0f)
            {
                screenWidth = ReferenceScreenWidth;
            }
            float bodyWidth = screenWidth - (RoomBodyInset * 2f);

            foreach (Image ground in _roomBackgrounds)
            {
                if (ground == null)
                {
                    continue;
                }
                if (_roomArt.RoomBackground != null)
                {
                    ground.sprite = _roomArt.RoomBackground;
                    ground.type = Image.Type.Simple;
                    // Stretched, not fitted: the art is drawn at the canvas
                    // ratio, and letterboxing a full-bleed ground would show
                    // the felt behind it.
                    ground.preserveAspect = false;
                }
                ground.color = Color.white;
            }

            RoomKit.SetHeroArt(_cutThroatHero, _roomArt.CutThroatTitle, screenWidth);
            RoomKit.SetHeroArt(_partnerHero, _roomArt.PartnerTitle, screenWidth);
            RoomKit.SetHeroArt(_oneLoveHero, _roomArt.OneLoveTitle, screenWidth);

            // The tile row sits inside a card, so it loses the card's padding too.
            float tileRowWidth = bodyWidth - (UiKit.CardPadding * 2f);
            foreach (GameObject tile in _classicTiles)
            {
                RoomKit.SetTileArt(tile, _roomArt.ClassicTile, tileRowWidth);
            }
            foreach (GameObject tile in _quickTiles)
            {
                RoomKit.SetTileArt(tile, _roomArt.QuickTile, tileRowWidth);
            }

            RoomKit.SetBoardArt(_cutThroatBoard, _roomArt.RewardsBoard, bodyWidth);
            RoomKit.SetBoardArt(_partnerBoard, _roomArt.RewardsBoard, bodyWidth);
            RoomKit.SetBoardArt(_oneLoveBoard, _roomArt.RewardsBoard, bodyWidth);
            RoomKit.SetBoardArt(_oneLovePartnerBoard, _roomArt.RewardsBoard, bodyWidth);
        }

        // Sets a block's background frame: the supplied wooden image (9-sliced so
        // the rivet end-caps stay crisp while the middle stretches) or the drawn
        // wood fallback.
        private void ApplyModeFrame(Image frame, Sprite? sprite)
        {
            if (sprite != null)
            {
                frame.sprite = sprite;
                frame.type = Image.Type.Sliced;
                frame.pixelsPerUnitMultiplier = ModeFrameSlicePpu;
            }
            else
            {
                frame.sprite = GradientSprite.RoundedDiagonal(0.2f, Hex("#7A5230"), Hex("#3A2614"));
                frame.type = Image.Type.Simple;
            }
        }

        /// <summary>
        /// The cursive font for the "Welcome to the Yard" title. Assign a TMP font
        /// asset (created from a cursive .ttf/.otf). Deferred like the logo — applied
        /// to the tagline built in <see cref="BuildYard"/>.
        /// </summary>
        public void SetTitleFont(TMP_FontAsset? font)
        {
            _titleFont = font;
            if (_taglineText != null && font != null)
            {
                _taglineText.font = font;
            }
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
            _friendsPanel = BuildFriendsPanel();
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
            _leaderboardScreen = BuildLeaderboardScreen();

            _countryPopup = BuildCountryPopup();
            _waitingOverlay = BuildWaitingOverlay();

            RefreshRooms();
            ShowTab(Tab.Yard, _yardPanel);
            RefreshOwnAvatars();
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

            // Header content sits slightly above centre so the taller bar reads
            // as a proper top region rather than a floating strip.
            const float rowY = 26f;

            // Profile picture (left) → Profile tab.
            GameObject pic = CreateChild(header.transform, "ProfilePic");
            RectTransform picRt = (RectTransform)pic.transform;
            picRt.anchorMin = new Vector2(0f, 0.5f);
            picRt.anchorMax = new Vector2(0f, 0.5f);
            picRt.pivot = new Vector2(0f, 0.5f);
            picRt.anchoredPosition = new Vector2(30f, rowY);
            picRt.sizeDelta = new Vector2(132f, 132f);
            Image picBg = pic.AddComponent<Image>();
            picBg.sprite = GradientSprite.RoundedDiagonal(0.5f, Hex("#FED100"), Hex("#009B3A"));
            picBg.color = Color.white;
            AddShadow(pic, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -3f));
            Button picBtn = pic.AddComponent<Button>();
            picBtn.targetGraphic = picBg;
            picBtn.onClick.AddListener(() => ShowTab(Tab.Profile, _profilePanel));
            AddIcon(pic.transform, IconFactory.Person(), 80f, ButtonTextColor);

            // Facebook profile picture overlay — hidden until it downloads, so the
            // person icon shows for guests / while loading.
            GameObject headerAv = CreateChild(pic.transform, "Avatar");
            RectTransform havRt = (RectTransform)headerAv.transform;
            havRt.anchorMin = Vector2.zero;
            havRt.anchorMax = Vector2.one;
            havRt.offsetMin = new Vector2(10f, 10f);
            havRt.offsetMax = new Vector2(-10f, -10f);
            _headerAvatar = headerAv.AddComponent<Image>();
            _headerAvatar.color = new Color(1f, 1f, 1f, 0f);
            _headerAvatar.preserveAspect = true;
            _headerAvatar.raycastTarget = false;

            // Coin value (center).
            GameObject coin = CreateChild(header.transform, "Coins");
            RectTransform coinRt = (RectTransform)coin.transform;
            coinRt.anchorMin = new Vector2(0.5f, 0.5f);
            coinRt.anchorMax = new Vector2(0.5f, 0.5f);
            coinRt.pivot = new Vector2(0.5f, 0.5f);
            coinRt.anchoredPosition = new Vector2(0f, rowY);
            coinRt.sizeDelta = new Vector2(480f, 100f);
            Image coinBg = coin.AddComponent<Image>();
            coinBg.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0f, 0f, 0f, 0.4f), new Color(0f, 0f, 0f, 0.25f));
            coinBg.color = Color.white;
            AddIconAt(coin.transform, IconFactory.Coin(), 58f, Hex("#FFD24A"), new Vector2(36f, 0f), TextAnchor.MiddleLeft);
            AddLabel(coin.transform, "10,000", 50f, CodeTextColor, TextAlignmentOptions.Center);

            // Gear (right) → Settings tab.
            GameObject gear = CreateChild(header.transform, "Gear");
            RectTransform gearRt = (RectTransform)gear.transform;
            gearRt.anchorMin = new Vector2(1f, 0.5f);
            gearRt.anchorMax = new Vector2(1f, 0.5f);
            gearRt.pivot = new Vector2(1f, 0.5f);
            gearRt.anchoredPosition = new Vector2(-30f, rowY);
            gearRt.sizeDelta = new Vector2(112f, 112f);
            Image gearBg = gear.AddComponent<Image>();
            gearBg.sprite = GradientSprite.RoundedDiagonal(0.4f, new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.08f));
            gearBg.color = Color.white;
            Button gearBtn = gear.AddComponent<Button>();
            gearBtn.targetGraphic = gearBg;
            gearBtn.onClick.AddListener(() => ShowTab(Tab.Settings, _settingsPanel));
            AddIcon(gear.transform, IconFactory.Gear(), 70f, BodyTextColor);

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
                OpenLeaderboard);
            CreatePill(bar.transform, IconFactory.Chart(), "Ranking", Hex("#3E8BFF"), Hex("#0B3F9E"),
                OpenLeaderboard);
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
            // Brown wooden bar with a gold top edge.
            Image bg = nav.AddComponent<Image>();
            bg.sprite = GradientSprite.Vertical(Hex("#3A2614"), Hex("#231409"));
            bg.color = Color.white;

            GameObject topEdge = CreateChild(nav.transform, "TopEdge");
            topEdge.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform ert = (RectTransform)topEdge.transform;
            ert.anchorMin = new Vector2(0f, 1f);
            ert.anchorMax = new Vector2(1f, 1f);
            ert.pivot = new Vector2(0.5f, 1f);
            ert.sizeDelta = new Vector2(0f, 3f);
            ert.anchoredPosition = Vector2.zero;
            Image edgeImg = topEdge.AddComponent<Image>();
            edgeImg.color = new Color(0.91f, 0.78f, 0.41f, 0.55f);
            edgeImg.raycastTarget = false;

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

            AddIconRow(stack.transform, icon, raised ? 72f : 60f, raised ? ButtonTextColor : BodyTextColor);
            AddLabelRow(stack.transform, label, 24f, raised ? ButtonTextColor : new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.85f));

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

        // ---- Yard (logo + scrollable vertical mode list) ------------------

        private static readonly Color Cream = new(0.957f, 0.906f, 0.788f);
        private const float ModeBlockHeight = 125f;
        private const float ModeBlockWidth = 470f;
        // wodden-block.png is 327px tall (transparent margins cropped off); scale
        // its 9-slice borders to the block height so the rivet end-caps render
        // crisp while the middle stretches.
        private const float ModeFrameSlicePpu = 327f / ModeBlockHeight;

        private void BuildYard()
        {
            _yardPanel = CreateContentPanel("YardPanel");

            // Logo tucked into the bottom-left corner, just above the bottom nav.
            GameObject logo = CreateChild(_yardPanel.transform, "Logo");
            RectTransform lrt = (RectTransform)logo.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
            lrt.pivot = new Vector2(0f, 0f);
            lrt.anchoredPosition = new Vector2(16f, 16f);
            lrt.sizeDelta = new Vector2(440f, 210f); // much bigger
            _logoImage = logo.AddComponent<Image>();
            _logoImage.preserveAspect = true;
            _logoImage.raycastTarget = false;
            _logoImage.sprite = _logoSprite;
            _logoImage.color = _logoSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);

            // Vertical mode list, centred on the Yard (no logo/tagline above it now).
            GameObject scroll = CreateChild(_yardPanel.transform, "ModeScroll");
            RectTransform srt = (RectTransform)scroll.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(0f, 150f); // higher up on the screen
            srt.sizeDelta = new Vector2(ModeBlockWidth + 70f, (ModeBlockHeight * 4f) + 40f);
            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 36f;

            GameObject viewport = CreateChild(scroll.transform, "Viewport");
            RectTransform vprt = (RectTransform)viewport.transform;
            vprt.anchorMin = Vector2.zero;
            vprt.anchorMax = Vector2.one;
            vprt.offsetMin = Vector2.zero;
            vprt.offsetMax = new Vector2(-18f, 0f); // room for the scrollbar
            Image vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            viewport.AddComponent<RectMask2D>();
            sr.viewport = vprt;

            GameObject content = CreateChild(viewport.transform, "Content");
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f; // boards very close together
            vlg.padding = new RectOffset(4, 4, 2, 12);
            vlg.childAlignment = TextAnchor.UpperCenter; // centre the narrower blocks
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            ContentSizeFitter cfit = content.AddComponent<ContentSizeFitter>();
            cfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = crt;

            // Gold scroll indicator on the right (auto-hides when nothing to scroll).
            Scrollbar bar = BuildVerticalScrollbar(scroll.transform);
            sr.verticalScrollbar = bar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // Same modes + functions as before, in the new wooden-teal block style.
            CreateModeBlock(content.transform, "Cut Throat", IconFactory.People(), () => ShowOverlay(_cutThroatScreen));
            CreateModeBlock(content.transform, "Partner", IconFactory.People(), () => ShowOverlay(_partnerScreen));
            CreateModeBlock(content.transform, "One-Love W/ Friends", IconFactory.People(), () => ShowOverlay(_friendsRoomScreen));
            CreateModeBlock(content.transform, "Practice", IconFactory.Person(), OnPracticeClicked);
        }

        private void OnPracticeClicked()
        {
            if (!_busy)
            {
                PracticeChosen?.Invoke();
            }
        }

        // A wooden-framed teal mode block: icon · title + subtitle · chevron, with
        // brass corner rivets and a top sheen. Full width in the vertical list.
        private void CreateModeBlock(Transform parent, string name, Sprite icon, Action onClick)
        {
            GameObject block = CreateChild(parent, $"Mode_{name}");
            LayoutElement le = block.AddComponent<LayoutElement>();
            le.preferredHeight = ModeBlockHeight;
            le.minHeight = ModeBlockHeight;
            le.preferredWidth = ModeBlockWidth; // fixed width, centred (not full-bleed)
            AddShadow(block, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -7f));

            // Background frame: the supplied wooden button image, or a drawn
            // wood/teal fallback until one is assigned (see SetModeButtonSprite).
            Image frame = block.AddComponent<Image>();
            frame.color = Color.white;
            ApplyModeFrame(frame, _modeButtonSprite);
            Button btn = block.AddComponent<Button>();
            btn.targetGraphic = frame;
            btn.onClick.AddListener(() => onClick());
            _modeFrames.Add(frame);

            // Drawn detail (teal inner + sheen + rivets), grouped so it can be
            // hidden wholesale once a wooden frame image is supplied.
            GameObject proc = CreateChild(block.transform, "Procedural");
            StretchFull((RectTransform)proc.transform);

            GameObject inner = CreateChild(proc.transform, "Inner");
            RectTransform irt = (RectTransform)inner.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(7f, 7f);
            irt.offsetMax = new Vector2(-7f, -7f);
            Image teal = inner.AddComponent<Image>();
            teal.sprite = GradientSprite.RoundedDiagonal(0.16f, Hex("#42BEB1"), Hex("#1F8A83"), Hex("#12615D"));
            teal.color = Color.white;
            teal.raycastTarget = false;

            GameObject sheen = CreateChild(inner.transform, "Sheen");
            RectTransform shrt = (RectTransform)sheen.transform;
            shrt.anchorMin = new Vector2(0f, 0.55f);
            shrt.anchorMax = new Vector2(1f, 1f);
            shrt.offsetMin = new Vector2(6f, 0f);
            shrt.offsetMax = new Vector2(-6f, -4f);
            Image sheenImg = sheen.AddComponent<Image>();
            sheenImg.sprite = GradientSprite.Vertical(new Color(1f, 1f, 1f, 0.26f), new Color(1f, 1f, 1f, 0f));
            sheenImg.raycastTarget = false;

            AddRivet(proc.transform, new Vector2(0f, 1f), new Vector2(14f, -14f));
            AddRivet(proc.transform, new Vector2(1f, 1f), new Vector2(-14f, -14f));
            AddRivet(proc.transform, new Vector2(0f, 0f), new Vector2(14f, 14f));
            AddRivet(proc.transform, new Vector2(1f, 0f), new Vector2(-14f, 14f));

            proc.SetActive(_modeButtonSprite == null);
            _modeProcedural.Add(proc);

            // Icon + title as one centred group, drawn on top of the frame — it
            // sits on the teal middle, clear of the wooden end-caps, and centres
            // regardless of label length. Kept on the block so it survives when
            // the drawn detail is hidden.
            GameObject row = CreateChild(block.transform, "Row");
            StretchFull((RectTransform)row.transform);
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleLeft; // left-align so every row's icon + text line up
            h.spacing = 18f;
            h.padding = new RectOffset(88, 18, 0, 0); // start past the left wooden end-cap
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            GameObject icoWrap = CreateChild(row.transform, "Ico");
            LayoutElement ile = icoWrap.AddComponent<LayoutElement>();
            ile.preferredWidth = 52f;
            ile.preferredHeight = 52f;
            AddIcon(icoWrap.transform, icon, 46f, Cream);

            GameObject titleGo = CreateChild(row.transform, "Title");
            TextMeshProUGUI title = titleGo.AddComponent<TextMeshProUGUI>();
            title.text = name;
            title.fontSize = 30f;
            title.fontStyle = FontStyles.Bold;
            title.color = Cream;
            title.alignment = TextAlignmentOptions.Left;
            title.raycastTarget = false;
        }

        private TextMeshProUGUI AddFixedLabel(
            Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            GameObject go = CreateChild(parent, "Label");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return tmp;
        }

        private void AddRivet(Transform parent, Vector2 anchor, Vector2 offset)
        {
            GameObject go = CreateChild(parent, "Rivet");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(11f, 11f);
            Image img = go.AddComponent<Image>();
            img.sprite = GradientSprite.RoundedDiagonal(0.5f, Hex("#F0D28A"), Hex("#A9791F"));
            img.color = Color.white;
            img.raycastTarget = false;
        }

        private Scrollbar BuildVerticalScrollbar(Transform parent)
        {
            GameObject go = CreateChild(parent, "Scrollbar");
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(10f, 0f);
            rt.anchoredPosition = Vector2.zero;
            Image track = go.AddComponent<Image>();
            track.sprite = GradientSprite.RoundedDiagonal(0.5f, new Color(0f, 0f, 0f, 0.35f), new Color(0f, 0f, 0f, 0.35f));
            track.color = Color.white;

            Scrollbar sb = go.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            GameObject area = CreateChild(go.transform, "Sliding Area");
            RectTransform art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(1f, 1f);
            art.offsetMax = new Vector2(-1f, -1f);

            GameObject handle = CreateChild(area.transform, "Handle");
            RectTransform hrt = (RectTransform)handle.transform;
            hrt.sizeDelta = Vector2.zero;
            Image hImg = handle.AddComponent<Image>();
            hImg.sprite = GradientSprite.RoundedDiagonal(0.5f, Hex("#F0D28A"), Hex("#A9791F"));
            hImg.color = Color.white;

            sb.handleRect = hrt;
            sb.targetGraphic = hImg;
            return sb;
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
                    // A country we cannot seat yet reads as unavailable, not absent.
                    Image? face = b.GetComponent<Image>();
                    if (face != null)
                    {
                        face.color = UnavailableTint;
                    }
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

        // ---- Game rooms ----------------------------------------------------
        //
        // Every room asks the same things in the same order — what format, who
        // is at the table, what it pays — and answers them with pictures rather
        // than boxed-off panels. A friends room asks one more question first,
        // because what kind of table it is changes everything below it.

        private GameObject BuildCutThroatScreen()
        {
            (GameObject screen, RectTransform body) = CreateRoomScreen(
                "CutThroatScreen", L10n.Get("room_cutthroat_title"), out Image hero);
            _cutThroatHero = hero;

            BuildFormatChoice(body, cutThroat: true);
            BuildSeatChoice(body, _cutThroatSeats);
            _cutThroatBoard = BuildRewardsBoard(
                body, GameMode.CutThroat,
                () => RoomSummary.For(GameMode.CutThroat, _selectedSize, _selectedFormat));

            UiKit.PrimaryButton(body, L10n.Get("room_start"),
                () => StartOnline(GameMode.CutThroat, _selectedSize, _selectedFormat));
            UiKit.Spring(body);
            return screen;
        }

        private GameObject BuildPartnerScreen()
        {
            (GameObject screen, RectTransform body) = CreateRoomScreen(
                "PartnerScreen", L10n.Get("room_partner_title"), out Image hero);
            _partnerHero = hero;

            // No table-size choice and no seat diagram: Partner is always four
            // seats with partners opposite, so there is nothing here to decide.
            BuildFormatChoice(body, cutThroat: false);
            _partnerBoard = BuildRewardsBoard(
                body, GameMode.Partner,
                () => RoomSummary.For(GameMode.Partner, NetworkedMatch.MaxPlayers, _selectedFormat));

            UiKit.PrimaryButton(body, L10n.Get("room_find_match"),
                () => StartOnline(GameMode.Partner, NetworkedMatch.MaxPlayers, _selectedFormat));
            UiKit.Spring(body);
            return screen;
        }

        private GameObject BuildFriendsRoomScreen()
        {
            (GameObject screen, RectTransform body) = CreateRoomScreen(
                "FriendsRoomScreen", L10n.Get("room_onelove_title"), out Image hero);
            _oneLoveHero = hero;

            // The first question, because the rest of the screen depends on it:
            // a Partner table has no size to pick and pays out to a side, not a
            // player, so the two rooms are built separately and swapped.
            RoomKit.Caption(body, L10n.Get("room_table_type"));
            RectTransform modeRow = RoomKit.ChoiceRow(body);
            _friendsModes.Add((
                RoomKit.WordOption(modeRow, L10n.Get("mode_cutthroat"),
                    () => ChooseFriendsMode(GameMode.CutThroat)),
                GameMode.CutThroat));
            _friendsModes.Add((
                RoomKit.WordOption(modeRow, L10n.Get("mode_partner"),
                    () => ChooseFriendsMode(GameMode.Partner)),
                GameMode.Partner));

            _friendsCutThroatSection = RoomKit.Section(body);
            BuildFormatChoice(_friendsCutThroatSection, cutThroat: true);
            BuildSeatChoice(_friendsCutThroatSection, _friendsSeats);
            _oneLoveBoard = BuildRewardsBoard(
                _friendsCutThroatSection, GameMode.CutThroat,
                () => RoomSummary.For(GameMode.CutThroat, _friendsSeatCount, _selectedFormat));

            _friendsPartnerSection = RoomKit.Section(body);
            BuildFormatChoice(_friendsPartnerSection, cutThroat: false);
            _oneLovePartnerBoard = BuildRewardsBoard(
                _friendsPartnerSection, GameMode.Partner,
                () => RoomSummary.For(GameMode.Partner, NetworkedMatch.MaxPlayers, _selectedFormat));

            UiKit.PrimaryButton(body, L10n.Get("room_create"), OnCreateRoomClicked);

            RectTransform join = UiKit.Card(body, L10n.Get("room_have_code"));
            BuildJoinRow(join);
            UiKit.Spring(body);
            return screen;
        }

        // ---- Room pieces ---------------------------------------------------

        /// <summary>
        /// A room's shell: the painted beach ground, the title art across the top
        /// with the back ring over it, and an empty body stack. Registered as an
        /// overlay so the Yard's back navigation already knows about it.
        /// </summary>
        private (GameObject screen, RectTransform body) CreateRoomScreen(
            string name, string fallbackTitle, out Image hero)
        {
            GameObject screen = CreateChild(transform, name);
            StretchFull((RectTransform)screen.transform);
            Image bg = screen.AddComponent<Image>();
            bg.sprite = GradientSprite.Vertical(Hex("#0A3D22"), Hex("#062A17"), Hex("#04160C"));
            bg.color = Color.white;
            bg.raycastTarget = true;
            _roomBackgrounds.Add(bg);

            (Image titleArt, RectTransform body) = RoomKit.Screen(
                (RectTransform)screen.transform, fallbackTitle, HideOverlays);
            hero = titleArt;

            screen.SetActive(false);
            _overlays.Add(screen);
            return (screen, body);
        }

        /// <summary>
        /// The format choice, made by picture. Every room that offers a choice
        /// shares <see cref="_selectedFormat"/>, so a player who prefers the
        /// short series does not have to say so twice. Only the naming differs:
        /// Partner's two are Classic Partner and Quick Partner.
        /// </summary>
        private void BuildFormatChoice(RectTransform parent, bool cutThroat)
        {
            RoomKit.Caption(parent, L10n.Get("room_game_format"));
            RectTransform row = RoomKit.ChoiceRow(parent);

            GameObject classic = RoomKit.Tile(
                row,
                L10n.Get(cutThroat ? "fmt_classic_six_love" : "fmt_classic_partner"),
                L10n.Get("fmt_classic_blurb"),
                () => ChooseFormat(MatchFormat.ClassicSixLove));
            GameObject quick = RoomKit.Tile(
                row,
                L10n.Get(cutThroat ? "fmt_quick_love" : "fmt_quick_partner"),
                L10n.Get("fmt_quick_blurb"),
                () => ChooseFormat(MatchFormat.QuickLove));

            _formatTiles.Add((classic, MatchFormat.ClassicSixLove));
            _formatTiles.Add((quick, MatchFormat.QuickLove));
            _classicTiles.Add(classic);
            _quickTiles.Add(quick);
        }

        /// <summary>How many people are at the table, counted in heads.</summary>
        private void BuildSeatChoice(RectTransform parent, List<(GameObject go, int seats)> options)
        {
            RoomKit.Caption(parent, L10n.Get("room_players"));
            RectTransform row = RoomKit.ChoiceRow(parent, spacing: 14f);

            for (int n = Stakes.MinSeats; n <= Stakes.MaxSeats; n++)
            {
                int seats = n;
                GameObject go = RoomKit.SeatOption(
                    row, seats, L10n.Get($"room_seat_{seats}"), () => ChooseSeats(options, seats));
                options.Add((go, seats));
            }
        }

        /// <summary>
        /// The stake, carved into the rewards board. A Partner table states the
        /// side's take and then each partner's half, because the pot alone would
        /// overstate what one player receives.
        /// </summary>
        private Image BuildRewardsBoard(RectTransform parent, GameMode mode, Func<RoomSummary> summary)
        {
            (Image board, RectTransform rows) = RoomKit.Board(parent, L10n.Get("room_rewards"));

            bool team = mode == GameMode.Partner;
            (_, TextMeshProUGUI entry) = RoomKit.BoardRow(
                rows, L10n.Get(team ? "room_entry_each" : "room_entry"));
            (_, TextMeshProUGUI take) = RoomKit.BoardRow(
                rows, L10n.Get(team ? "room_team_takes" : "room_winner_takes"));
            (_, TextMeshProUGUI extra) = RoomKit.BoardRow(
                rows, L10n.Get(team ? "room_your_share" : "room_key_bonus"));

            _roomRefreshers.Add(() =>
            {
                RoomSummary s = summary();
                entry.text = Coins(s.Entry);
                take.text = Coins(s.WinningSideTakes);
                extra.text = team ? Coins(s.ShareEach) : "+" + Coins(s.KeyBonus);
            });
            return board;
        }

        /// <summary>The code field and its Join button, for a friends table.</summary>
        private void BuildJoinRow(RectTransform card)
        {
            GameObject row = UiKit.Child(card, "JoinRow");
            row.AddComponent<LayoutElement>().preferredHeight = 84f;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            GameObject inputGo = UiKit.Child(row.transform, "CodeInput");
            inputGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Image inputBg = inputGo.AddComponent<Image>();
            inputBg.sprite = GradientSprite.RoundedDiagonal(0.2f, Color.white, Color.white);
            inputBg.type = Image.Type.Sliced;
            inputBg.color = InputBgColor;

            _codeInput = inputGo.AddComponent<TMP_InputField>();
            _codeInput.targetGraphic = inputBg;
            _codeInput.characterLimit = RoomCodeGenerator.CodeLength;
            _codeInput.contentType = TMP_InputField.ContentType.Alphanumeric;

            GameObject textArea = UiKit.Child(inputGo.transform, "TextArea");
            RectTransform taRt = (RectTransform)textArea.transform;
            UiKit.Stretch(taRt, left: 16f, right: 16f, inset: 4f);
            textArea.AddComponent<RectMask2D>();

            GameObject textGo = UiKit.Child(textArea.transform, "Text");
            StretchFull((RectTransform)textGo.transform);
            TextMeshProUGUI textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;
            textTmp.fontSize = 26f;
            textTmp.color = UiKit.Bone;

            GameObject ph = UiKit.Child(textArea.transform, "Placeholder");
            StretchFull((RectTransform)ph.transform);
            TextMeshProUGUI phTmp = ph.AddComponent<TextMeshProUGUI>();
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            phTmp.fontSize = 26f;
            phTmp.color = UiKit.Muted;
            phTmp.text = L10n.Get("room_code_placeholder");

            _codeInput.textViewport = taRt;
            _codeInput.textComponent = textTmp;
            _codeInput.placeholder = phTmp;

            GameObject join = UiKit.GhostButton(
                (RectTransform)row.transform, L10n.Get("room_join"), OnSubmitJoinClicked);
            LayoutElement joinLe = join.GetComponent<LayoutElement>();
            joinLe.preferredWidth = 150f;
            joinLe.flexibleWidth = 0f;
        }

        // ---- Room selection -------------------------------------------------

        private void ChooseFormat(MatchFormat format)
        {
            _selectedFormat = format;
            RefreshRooms();
        }

        private void ChooseSeats(List<(GameObject go, int seats)> options, int seats)
        {
            if (ReferenceEquals(options, _friendsSeats))
            {
                _friendsSeatCount = seats;
            }
            else
            {
                _selectedSize = seats;
            }
            RefreshRooms();
        }

        private void ChooseFriendsMode(GameMode mode)
        {
            _createMode = mode;
            RefreshRooms();
        }

        /// <summary>
        /// Restates every room's choices and every number that follows from
        /// them, and shows the half of the friends room its table type calls
        /// for. Cheap enough to run on any tap.
        /// </summary>
        private void RefreshRooms()
        {
            RoomKit.Refresh(_formatTiles, _selectedFormat);
            RoomKit.Refresh(_cutThroatSeats, _selectedSize);
            RoomKit.Refresh(_friendsSeats, _friendsSeatCount);
            RoomKit.Refresh(_friendsModes, _createMode);

            _friendsCutThroatSection?.gameObject.SetActive(_createMode == GameMode.CutThroat);
            _friendsPartnerSection?.gameObject.SetActive(_createMode == GameMode.Partner);

            foreach (Action refresh in _roomRefreshers)
            {
                refresh();
            }
        }

        private static string Coins(int amount) => amount.ToString("N0", CultureInfo.CurrentCulture);

        private void OnCreateRoomClicked()
        {
            if (_busy)
            {
                return;
            }

            int seats = _createMode == GameMode.Partner ? NetworkedMatch.MaxPlayers : _friendsSeatCount;
            StartCreate(seats, _createMode, _selectedFormat);
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
            RectTransform body = UiKit.Screen(
                (RectTransform)screen.transform, "Profile", () => ShowTab(Tab.Yard, _yardPanel));

            // Identity first, then how you have been doing.
            RectTransform who = UiKit.Card(body);
            GameObject idRow = UiKit.Child(who, "Identity");
            idRow.AddComponent<LayoutElement>().preferredHeight = 140f;
            HorizontalLayoutGroup idLayout = idRow.AddComponent<HorizontalLayoutGroup>();
            idLayout.spacing = 22f;
            idLayout.childAlignment = TextAnchor.MiddleLeft;
            idLayout.childControlWidth = true;
            idLayout.childControlHeight = true;
            idLayout.childForceExpandWidth = false;
            idLayout.childForceExpandHeight = false;

            _profileAvatar = MakeAvatar(idRow.transform, 128f, string.Empty);
            LayoutElement avatarSize = _profileAvatar.gameObject.AddComponent<LayoutElement>();
            avatarSize.preferredWidth = 128f;
            avatarSize.preferredHeight = 128f;

            GameObject names = UiKit.Child(idRow.transform, "Names");
            names.AddComponent<LayoutElement>().flexibleWidth = 1f;
            VerticalLayoutGroup nameStack = names.AddComponent<VerticalLayoutGroup>();
            nameStack.spacing = 2f;
            nameStack.childAlignment = TextAnchor.MiddleLeft;
            nameStack.childControlWidth = true;
            nameStack.childControlHeight = true;
            nameStack.childForceExpandWidth = true;
            nameStack.childForceExpandHeight = false;

            _profileName = UiKit.Label(
                names.transform, "—", 34f, UiKit.Bone, TextAlignmentOptions.MidlineLeft);
            _profileName.fontStyle = FontStyles.Bold;
            _profileTag = UiKit.Label(
                names.transform, string.Empty, 23f, UiKit.BrassLit, TextAlignmentOptions.MidlineLeft);

            // Tiles rather than a list: these are glanced at, not read.
            RectTransform grid = UiKit.Grid2(body, cellHeight: 104f);
            _statCoins = UiKit.StatTile(grid, "Coins", "—");
            _statWinRate = UiKit.StatTile(grid, "Win rate", "—");
            _statGames = UiKit.StatTile(grid, "Played", "—");
            _statWins = UiKit.StatTile(grid, "Wins", "—");

            RectTransform account = UiKit.Card(body, L10n.Get("account_section"));
            _accountStatusLabel = UiKit.Row(account, "Status", string.Empty);

            GameObject action = UiKit.GhostButton(account, string.Empty, OnAccountActionClicked);
            _accountActionLabel = action.GetComponentInChildren<TextMeshProUGUI>();

            UiKit.Spring(body);
            UiKit.GhostButton(body, L10n.Get("account_logout"), OnLogoutClicked);

            RefreshAccountSection();
            return screen;
        }

        private static string AccountStatusLabel()
        {
            AuthService? auth = AuthService.Instance;
            if (auth == null || !auth.IsSignedIn || auth.IsGuest)
            {
                return L10n.Get("account_status_guest");
            }
            if (auth.IsFacebookLinked)
            {
                return L10n.Get("account_status_facebook");
            }
            return L10n.Get("account_status_email");
        }

        private void RefreshAccountSection()
        {
            AuthService? auth = AuthService.Instance;
            bool linked = auth != null && auth.IsFacebookLinked;
            if (_accountStatusLabel != null)
            {
                _accountStatusLabel.text = AccountStatusLabel();
            }
            if (_accountActionLabel != null)
            {
                _accountActionLabel.text = L10n.Get(
                    linked ? "account_disconnect_facebook" : "account_connect_facebook");
            }
        }

        private async void OnAccountActionClicked()
        {
            AuthService? auth = AuthService.Instance;
            if (auth == null)
            {
                return;
            }
            try
            {
                if (auth.IsFacebookLinked)
                {
                    if (auth.HasEmail)
                    {
                        await auth.UnlinkFacebookAsync();
                        RefreshAccountSection();
                    }
                    else
                    {
                        // Facebook is the only credential — disconnecting it would
                        // orphan the account, so treat it as a sign-out.
                        auth.SignOut();
                        LoggedOut?.Invoke();
                    }
                }
                else
                {
                    bool connected = await auth.ConnectFacebookAsync();
                    if (connected)
                    {
                        RefreshAccountSection();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] account action failed: {ex.Message}");
            }
        }

        private void OnLogoutClicked()
        {
            AuthService.Instance?.SignOut();
            LoggedOut?.Invoke();
        }

        // Loads the signed-in player's own Facebook picture into the header + the
        // profile card (the friends/leaderboard rows use their own server URLs).
        private void RefreshOwnAvatars()
        {
            string? url = AuthService.Instance?.PhotoUrl;
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            if (_headerAvatar != null)
            {
                LoadAvatarInto(_headerAvatar, url);
            }
            if (_profileAvatar != null)
            {
                LoadAvatarInto(_profileAvatar, url);
            }
        }

        private async void LoadProfileStats()
        {
            try
            {
                SocialService.ProfileCard card = await SocialService.GetProfileAsync();
                RefreshOwnAvatars();
                if (_statCoins != null)
                {
                    _statCoins.text = card.Coins.ToString("N0");
                }
                if (_statGames != null)
                {
                    _statGames.text = card.MatchesPlayed.ToString();
                }
                if (_statWins != null)
                {
                    _statWins.text = card.Wins.ToString();
                }
                if (_statWinRate != null)
                {
                    _statWinRate.text = card.MatchesPlayed > 0
                        ? $"{Mathf.RoundToInt(card.WinRate * 100f)}%"
                        : "—";
                }
                if (_profileName != null)
                {
                    _profileName.text = string.IsNullOrEmpty(card.Name)
                        ? ProfileService.Instance?.Profile?.DisplayName ?? "—"
                        : card.Name;
                }
                if (_profileTag != null)
                {
                    // The tagname slice is not built yet, so fall back to the
                    // display name rather than showing an empty line.
                    _profileTag.text = $"@{(_profileName != null ? _profileName.text : string.Empty)}"
                        .Replace(" ", string.Empty).ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] profile load failed: {ex.Message}");
            }
        }

        // ---- Friends + leaderboard (M7 phase 2) ---------------------------

        private GameObject BuildFriendsPanel()
        {
            GameObject screen = CreateContentPanel("FriendsPanel");
            CreateTitle(screen.transform, L10n.Get("friends_title"), -30f, 56f);

            GameObject bodyGo = CreateChild(screen.transform, "Body");
            RectTransform brt = (RectTransform)bodyGo.transform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(20f, 190f);
            brt.offsetMax = new Vector2(-20f, -150f);
            (Transform content, TextMeshProUGUI status) = CreateListArea(brt, 0f);
            _friendsContent = content;
            _friendsStatus = status;

            _inviteStatus = AddColumnLabel(screen.transform, string.Empty);
            RectTransform isrt = (RectTransform)_inviteStatus.transform;
            isrt.anchorMin = new Vector2(0.5f, 0f);
            isrt.anchorMax = new Vector2(0.5f, 0f);
            isrt.pivot = new Vector2(0.5f, 0f);
            isrt.anchoredPosition = new Vector2(0f, 132f);
            isrt.sizeDelta = new Vector2(600f, 44f);

            // One action button: Invite (when Facebook-connected) or Connect.
            _friendsActionButton = CreateButton(string.Empty, OnFriendsActionClicked);
            _friendsActionButton.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform abrt = (RectTransform)_friendsActionButton.transform;
            abrt.anchorMin = new Vector2(0.5f, 0f);
            abrt.anchorMax = new Vector2(0.5f, 0f);
            abrt.pivot = new Vector2(0.5f, 0f);
            abrt.anchoredPosition = new Vector2(0f, 40f);
            abrt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            _friendsActionLabel = _friendsActionButton.GetComponentInChildren<TextMeshProUGUI>();
            return screen;
        }

        private GameObject BuildLeaderboardScreen()
        {
            (GameObject screen, RectTransform body) = CreateFullScreen("LeaderboardScreen", "Leaderboard");

            GameObject toggle = CreateChild(body, "ScopeToggle");
            RectTransform trt = (RectTransform)toggle.transform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -84f);
            trt.offsetMax = Vector2.zero;
            trt.sizeDelta = new Vector2(0f, 84f);
            HorizontalLayoutGroup hlg = toggle.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 20f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            MakeToggleButton(toggle.transform, L10n.Get("lb_global"), () => LoadLeaderboard("global"));
            MakeToggleButton(toggle.transform, L10n.Get("lb_friends"), () => LoadLeaderboard("friends"));

            (Transform content, TextMeshProUGUI status) = CreateListArea(body, 100f);
            _leaderboardContent = content;
            _leaderboardStatus = status;
            return screen;
        }

        private void OpenLeaderboard()
        {
            ShowOverlay(_leaderboardScreen);
            LoadLeaderboard(_leaderboardScope);
        }

        private async void LoadLeaderboard(string scope)
        {
            _leaderboardScope = scope;
            if (_leaderboardContent == null || _leaderboardStatus == null)
            {
                return;
            }
            ClearChildren(_leaderboardContent);
            _leaderboardStatus.gameObject.SetActive(true);
            _leaderboardStatus.text = L10n.Get("lb_loading");
            try
            {
                List<SocialService.LeaderRow> rows;
                if (scope == "friends")
                {
                    if (AuthService.Instance == null || !AuthService.Instance.IsFacebookLinked)
                    {
                        _leaderboardStatus.text = L10n.Get("lb_friends_connect");
                        return;
                    }
                    List<SocialService.Friend> friends = await SocialService.GetPlayingFriendsAsync();
                    List<string> uids = new();
                    foreach (SocialService.Friend f in friends)
                    {
                        uids.Add(f.Uid);
                    }
                    rows = await SocialService.GetLeaderboardAsync("friends", "wins", uids);
                }
                else
                {
                    rows = await SocialService.GetLeaderboardAsync("global", "wins", null);
                }

                if (rows.Count == 0)
                {
                    _leaderboardStatus.text = L10n.Get("lb_empty");
                    return;
                }
                _leaderboardStatus.gameObject.SetActive(false);
                foreach (SocialService.LeaderRow r in rows)
                {
                    AddLeaderRow(_leaderboardContent, r.Rank, r.Name, r.PhotoURL, L10n.Get("lb_wins_fmt", r.Wins), r.IsSelf);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] leaderboard load failed: {ex.Message}");
                if (_leaderboardStatus != null)
                {
                    _leaderboardStatus.text = L10n.Get("lb_error");
                }
            }
        }

        private async void LoadFriends()
        {
            if (_friendsContent == null || _friendsStatus == null)
            {
                return;
            }
            ClearChildren(_friendsContent);
            bool linked = AuthService.Instance != null && AuthService.Instance.IsFacebookLinked;
            _friendsActionButton?.SetActive(true);
            if (_friendsActionLabel != null)
            {
                _friendsActionLabel.text = L10n.Get(linked ? "invite_button" : "account_connect_facebook");
            }
            _friendsStatus.gameObject.SetActive(true);
            if (!linked)
            {
                _friendsStatus.text = L10n.Get("friends_connect");
                return;
            }
            _friendsStatus.text = L10n.Get("friends_loading");
            try
            {
                List<SocialService.Friend> friends = await SocialService.GetPlayingFriendsAsync();
                if (friends.Count == 0)
                {
                    _friendsStatus.text = L10n.Get("friends_empty");
                    return;
                }
                _friendsStatus.gameObject.SetActive(false);
                int rank = 1;
                foreach (SocialService.Friend f in friends)
                {
                    AddLeaderRow(_friendsContent, rank, f.Name, f.PhotoURL, L10n.Get("lb_wins_fmt", f.Wins), false);
                    rank++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] friends load failed: {ex.Message}");
                if (_friendsStatus != null)
                {
                    _friendsStatus.text = L10n.Get("friends_error");
                }
            }
        }

        private async void OnConnectFacebookClicked()
        {
            AuthService? auth = AuthService.Instance;
            if (auth == null)
            {
                return;
            }
            try
            {
                bool connected = await auth.ConnectFacebookAsync();
                if (connected)
                {
                    RefreshAccountSection();
                    LoadFriends();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] connect Facebook failed: {ex.Message}");
            }
        }

        // The Friends-tab action button: invite (when connected) or connect.
        private void OnFriendsActionClicked()
        {
            if (AuthService.Instance != null && AuthService.Instance.IsFacebookLinked)
            {
                OnInviteClicked();
            }
            else
            {
                OnConnectFacebookClicked();
            }
        }

        private async void OnInviteClicked()
        {
            if (_inviteStatus == null)
            {
                return;
            }
            _inviteStatus.text = L10n.Get("invite_sending");
            try
            {
                SocialService.InviteResult result = await SocialService.SendInviteAndClaimAsync(
                    L10n.Get("invite_message"), L10n.Get("invite_title"));
                string message = result.Outcome switch
                {
                    "ok" => L10n.Get("invite_rewarded", result.RemainingToday),
                    "cap" => L10n.Get("invite_cap"),
                    "duplicate" => L10n.Get("invite_duplicate"),
                    "cancelled" => L10n.Get("invite_cancelled"),
                    _ => L10n.Get("invite_error"),
                };
                if (_inviteStatus != null)
                {
                    _inviteStatus.text = message;
                }
                if (result.Rewarded)
                {
                    LoadProfileStats();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyView] invite failed: {ex.Message}");
                if (_inviteStatus != null)
                {
                    _inviteStatus.text = L10n.Get("invite_error");
                }
            }
        }

        // A vertical scrollable list filling the given region, with a centered
        // status label for loading/empty/error states. Returns (content, status).
        private (Transform content, TextMeshProUGUI status) CreateListArea(RectTransform region, float topReserve)
        {
            GameObject statusGo = CreateChild(region, "ListStatus");
            RectTransform strt = (RectTransform)statusGo.transform;
            strt.anchorMin = strt.anchorMax = new Vector2(0.5f, 0.5f);
            strt.pivot = new Vector2(0.5f, 0.5f);
            strt.anchoredPosition = Vector2.zero;
            strt.sizeDelta = new Vector2(560f, 120f);
            TextMeshProUGUI status = statusGo.AddComponent<TextMeshProUGUI>();
            status.alignment = TextAlignmentOptions.Center;
            status.fontSize = 26f;
            status.color = BodyTextColor;
            status.textWrappingMode = TextWrappingModes.Normal;
            status.raycastTarget = false;

            GameObject scroll = CreateChild(region, "Scroll");
            RectTransform srt = (RectTransform)scroll.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = new Vector2(0f, -topReserve);
            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 36f;

            GameObject viewport = CreateChild(scroll.transform, "Viewport");
            RectTransform vprt = (RectTransform)viewport.transform;
            vprt.anchorMin = Vector2.zero;
            vprt.anchorMax = Vector2.one;
            vprt.offsetMin = Vector2.zero;
            vprt.offsetMax = new Vector2(-18f, 0f);
            Image vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            viewport.AddComponent<RectMask2D>();
            sr.viewport = vprt;

            GameObject content = CreateChild(viewport.transform, "Content");
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(6, 6, 6, 12);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            ContentSizeFitter cfit = content.AddComponent<ContentSizeFitter>();
            cfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = crt;

            return (content.transform, status);
        }

        private void AddLeaderRow(Transform parent, int rank, string name, string photoURL, string detail, bool isSelf)
        {
            GameObject row = CreateChild(parent, "LbRow");
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredWidth = 640f;
            le.preferredHeight = 84f;
            Image bg = row.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(
                0.25f,
                isSelf ? Hex("#1FA845") : Hex("#0C3325"),
                isSelf ? Hex("#0F7A33") : Hex("#082418"));
            bg.color = Color.white;
            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(20, 20, 0, 0);
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            AddCell(row.transform, rank.ToString(), 28f, CodeTextColor, 44f, TextAlignmentOptions.Center);
            MakeAvatar(row.transform, 60f, photoURL);
            AddCell(row.transform, name, 28f, BodyTextColor, 300f, TextAlignmentOptions.Left);
            AddCell(row.transform, detail, 24f, CodeTextColor, 140f, TextAlignmentOptions.Right);
        }

        // A round-ish avatar image that starts as a person placeholder and swaps
        // to the downloaded profile picture when it arrives (M7).
        private Image MakeAvatar(Transform parent, float size, string photoURL)
        {
            GameObject go = CreateChild(parent, "Avatar");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            Image img = go.AddComponent<Image>();
            img.sprite = IconFactory.Person();
            img.color = new Color(1f, 1f, 1f, 0.7f);
            img.preserveAspect = true;
            img.raycastTarget = false;
            LoadAvatarInto(img, photoURL);
            return img;
        }

        private static async void LoadAvatarInto(Image target, string url)
        {
            Sprite? sprite = await AvatarLoader.LoadAsync(url);
            // Unity's overloaded null check catches a target destroyed while the
            // image was downloading (panel rebuilt, tab switched).
            if (sprite != null && target != null)
            {
                target.sprite = sprite;
                target.color = Color.white;
            }
        }

        private void AddCell(Transform parent, string text, float size, Color color, float width, TextAlignmentOptions align)
        {
            GameObject go = CreateChild(parent, "Cell");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 60f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
        }

        private void MakeToggleButton(Transform parent, string label, Action onClick)
        {
            GameObject go = CreateChild(parent, $"Toggle_{label}");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 220f;
            le.preferredHeight = 66f;
            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.4f, Hex("#0E4A31"), Hex("#083120"));
            bg.color = Color.white;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());
            AddLabel(go.transform, label, 28f, BodyTextColor, TextAlignmentOptions.Center);
        }

        private TextMeshProUGUI AddColumnLabel(Transform parent, string text)
        {
            GameObject go = CreateChild(parent, "AccountStatus");
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = 40f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 24f;
            tmp.color = BodyTextColor;
            tmp.text = text;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Destroy(t.GetChild(i).gameObject);
            }
        }

        private GameObject BuildSettingsPanel()
        {
            GameObject screen = CreateContentPanel("SettingsPanel");
            RectTransform body = UiKit.Screen(
                (RectTransform)screen.transform, "Settings", () => ShowTab(Tab.Yard, _yardPanel));

            // Volumes are sliders, not on/off: the reason to open this screen is
            // almost always "quieter", rarely "silent".
            RectTransform sound = UiKit.Card(body, "Sound");
            UiKit.VolumeRow(sound, "Effects", GameSettings.EffectsVolume,
                v => GameSettings.EffectsVolume = v);
            UiKit.VolumeRow(sound, "Music", GameSettings.MusicVolume,
                v => GameSettings.MusicVolume = v);
            UiKit.VolumeRow(sound, "Notifications", GameSettings.NotificationVolume,
                v => GameSettings.NotificationVolume = v);

            RectTransform feel = UiKit.Card(body, "Feel");
            UiKit.SwitchRow(feel, "Vibration", Haptics.VibrationEnabled,
                v => Haptics.VibrationEnabled = v);
            // Says what it cannot do, rather than letting someone switch it off
            // and then wonder why their phone still buzzes on their turn.
            UiKit.Label(feel, "Turn nudges stay on \u2014 the table is waiting on you.",
                20f, UiKit.Muted, TextAlignmentOptions.MidlineLeft)
                .textWrappingMode = TextWrappingModes.Normal;

            RectTransform language = UiKit.Card(body, "Language");
            UiKit.Segment(language, LanguageNames, _selectedLanguage, OnLanguageSelected);

            UiKit.Spring(body);
            UiKit.GhostButton(body, "How to Play", () => ShowOverlay(_rulesScreen));
            return screen;
        }

        /// <summary>The locales shipped today; see Assets/_Project/Localization.</summary>
        private static readonly string[] LanguageNames = { "English", "Espa\u00f1ol", "Fran\u00e7ais" };

        private void OnLanguageSelected(int index)
        {
            _selectedLanguage = index;
            Debug.Log($"[LobbyView] language -> {LanguageNames[index]}");
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

        private TextMeshProUGUI StatRow(Transform parent, string label, string value)
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
            return vt;
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

            // Lazy-load social data when its tab opens (M7).
            if (tab == Tab.Friends)
            {
                LoadFriends();
            }
            else if (tab == Tab.Profile)
            {
                RefreshAccountSection();
                LoadProfileStats();
            }
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
            if (code.Length != RoomCodeGenerator.CodeLength)
            {
                EnterWaitingState(L10n.Get("room_code_wrong_length", RoomCodeGenerator.CodeLength));
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

        /// <summary>
        /// Shows a failure in the waiting overlay and releases the busy state, so
        /// the player can back out. Public because a join can fail after the
        /// lobby has handed off — the table never seated us — and the lobby is
        /// still the screen in front of them.
        /// </summary>
        public void FailWaiting(string error)
        {
            _busy = false;
            if (_waitingStatus != null)
            {
                _waitingStatus.text = error;
                _waitingStatus.color = StatusErrorColor;
            }
        }

        // ---- Selection toggles --------------------------------------------

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
