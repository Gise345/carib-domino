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
    /// Cinematic front-of-house. A stack of full-screen screens over a shared
    /// background photo + darkening scrim, one active at a time:
    /// <list type="bullet">
    ///   <item><b>Hub</b> — a 2×2 grid of country blocks; Jamaica is live.</item>
    ///   <item><b>Jamaica menu</b> — big mode blocks: Cut Throat Online, Partner,
    ///         One-Love With Friends, Practice.</item>
    ///   <item><b>Cut Throat / Partner / Friends</b> — one screen per mode with
    ///         its own options (format, size, rewards, create/join).</item>
    /// </list>
    /// A shared waiting overlay (status + Cancel) covers whichever screen is
    /// active while matchmaking. Bubbles <see cref="PracticeChosen"/>,
    /// <see cref="OnlineRoomActive"/> and <see cref="WaitingCancelled"/>.
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
        private static readonly Color SelectedTint = Color.white;
        private static readonly Color UnselectedTint = new(0.5f, 0.5f, 0.5f, 1f);

        private const float TitleFontSize = 72f;
        private const float ButtonFontSize = 28f;
        private const float ButtonWidth = 440f;
        private const float ButtonHeight = 84f;

        // Bump every build; renders faintly in the lobby corner so we can confirm
        // the running binary matches the source.
        private const string BuildStamp = "build redesign · One-Love";

        public event Action? PracticeChosen;

        /// <summary>
        /// Fires with the room code and, for the creator, the chosen player count
        /// (2–4), game mode and Cut-Throat series format. Joiners pass count 0 and
        /// placeholders — the real values arrive from the host over the network.
        /// </summary>
        public event Action<string, int, GameMode, MatchFormat>? OnlineRoomActive;

        /// <summary>Fires when the player backs out of the waiting room before the deal.</summary>
        public event Action? WaitingCancelled;

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

        // Screens (exactly one active).
        private GameObject? _hubScreen;
        private GameObject? _comingSoonScreen;
        private GameObject? _jamaicaMenuScreen;
        private GameObject? _cutThroatScreen;
        private GameObject? _partnerScreen;
        private GameObject? _friendsScreen;

        // Coming-soon screen, re-themed per country on show.
        private Image? _comingSoonHeader;
        private TextMeshProUGUI? _comingSoonTitle;

        // Shared waiting overlay.
        private GameObject? _waitingOverlay;
        private TextMeshProUGUI? _waitingStatus;

        // Selection state for the online screens.
        private MatchFormat _selectedFormat = MatchFormat.ClassicSixLove;
        private int _selectedSize = 2;
        private GameMode _createMode = GameMode.CutThroat;

        private readonly List<(GameObject go, MatchFormat fmt)> _formatButtons = new();
        private readonly List<(GameObject go, int size)> _sizeButtons = new();
        private readonly List<(GameObject go, GameMode mode)> _createModeButtons = new();

        // Friends (private) screen bits toggled by the create mode.
        private GameObject? _friendsFormatRow;
        private GameObject? _friendsSizeRow;
        private TMP_InputField? _codeInput;

        private bool _busy;
        private Image? _backgroundImage;

        private void Awake()
        {
            _countries = new[]
            {
                new Country("Jamaica", true,
                    new[] { Hex("#FED100"), Hex("#009B3A"), Hex("#05351C") }),
                new Country("Cuba", false,
                    new[] { Hex("#0A2A8F"), Hex("#CF142B"), Hex("#160308") }),
                new Country("Mexico", false,
                    new[] { Hex("#006847"), Hex("#CE1126"), Hex("#160308") }),
                new Country("Dominican Rep.", false,
                    new[] { Hex("#002D62"), Hex("#CE1126"), Hex("#0A0410") }),
            };

            BuildLayout();
        }

        /// <summary>Applies the shared background photo. Null restores the felt fill.</summary>
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

            GameObject scrim = new("Scrim", typeof(RectTransform));
            scrim.transform.SetParent(transform, worldPositionStays: false);
            StretchFull((RectTransform)scrim.transform);
            Image scrimImg = scrim.AddComponent<Image>();
            scrimImg.sprite = GradientSprite.Vertical(
                new Color(0f, 0f, 0f, 0.78f), new Color(0f, 0f, 0f, 0.35f), new Color(0f, 0f, 0f, 0.86f));
            scrimImg.color = Color.white;
            scrimImg.raycastTarget = false;

            _hubScreen = BuildHub();
            _comingSoonScreen = BuildComingSoon();
            _jamaicaMenuScreen = BuildJamaicaMenu();
            _cutThroatScreen = BuildCutThroatScreen();
            _partnerScreen = BuildPartnerScreen();
            _friendsScreen = BuildFriendsScreen();
            _waitingOverlay = BuildWaitingOverlay();

            RefreshFormatButtons();
            RefreshSizeButtons();
            RefreshCreateModeButtons();
            ShowHub();
        }

        // ---- Hub (countries) ----------------------------------------------

        private GameObject BuildHub()
        {
            GameObject screen = CreateScreen("HubScreen");

            CreateTitle(screen.transform, "POSE", -60f, TitleFontSize);
            CreateSubtitle(screen.transform, "CHOOSE YOUR TABLE", -168f);

            GameObject stamp = new("BuildStamp", typeof(RectTransform));
            stamp.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform stampRt = (RectTransform)stamp.transform;
            stampRt.anchorMin = new Vector2(0f, 0f);
            stampRt.anchorMax = new Vector2(1f, 0f);
            stampRt.pivot = new Vector2(0.5f, 0f);
            stampRt.anchoredPosition = new Vector2(0f, 8f);
            stampRt.sizeDelta = new Vector2(-20f, 22f);
            TextMeshProUGUI stampTmp = stamp.AddComponent<TextMeshProUGUI>();
            stampTmp.alignment = TextAlignmentOptions.Center;
            stampTmp.fontSize = 15f;
            stampTmp.color = new Color(1f, 1f, 1f, 0.5f);
            stampTmp.text = BuildStamp;
            stampTmp.raycastTarget = false;

            GameObject grid = CreateGrid(screen.transform, new Vector2(0f, -30f), new Vector2(300f, 300f));
            for (int i = 0; i < _countries.Length; i++)
            {
                int idx = i;
                Country c = _countries[i];
                CreateBlock(grid.transform, c.Name, c.Live ? "PLAY" : "COMING SOON", c.Colors, c.Live,
                    () => OnCountryClicked(idx));
            }
            return screen;
        }

        private void OnCountryClicked(int index)
        {
            if (_busy)
            {
                return;
            }
            if (_countries[index].Live)
            {
                ShowJamaicaMenu();
            }
            else
            {
                ShowComingSoon(index);
            }
        }

        // ---- Jamaica menu (mode blocks) -----------------------------------

        private GameObject BuildJamaicaMenu()
        {
            GameObject screen = CreateScreen("JamaicaMenu");
            CreateBackButton(screen.transform, ShowHub);
            CreateTitle(screen.transform, "Jamaica", -70f, 60f);

            GameObject grid = CreateGrid(screen.transform, new Vector2(0f, -30f), new Vector2(320f, 300f));

            CreateBlock(grid.transform, "Cut Throat\nOnline", "Ranked · 2-4",
                new[] { Hex("#FED100"), Hex("#009B3A"), Hex("#05351C") }, true, ShowCutThroat);
            CreateBlock(grid.transform, "Partner", "2 v 2 teams",
                new[] { Hex("#00A651"), Hex("#0B3D1E"), Hex("#04120A") }, true, ShowPartner);
            CreateBlock(grid.transform, "One-Love\nWith Friends", "Private room",
                new[] { Hex("#F7B500"), Hex("#B26A00"), Hex("#3A2200") }, true, ShowFriends);
            CreateBlock(grid.transform, "Practice", "vs Bots · free",
                new[] { Hex("#4A5568"), Hex("#2D3748"), Hex("#12161F") }, true, OnPracticeClicked);

            return screen;
        }

        private void OnPracticeClicked()
        {
            if (!_busy)
            {
                PracticeChosen?.Invoke();
            }
        }

        // ---- Cut Throat Online screen -------------------------------------

        private GameObject BuildCutThroatScreen()
        {
            GameObject screen = CreateScreen("CutThroatScreen");
            CreateBackButton(screen.transform, ShowJamaicaMenu);
            CreateTitle(screen.transform, "Cut Throat Online", -70f, 52f);

            GameObject col = CreateColumn(screen.transform);

            CreateSectionLabel(col.transform, "FORMAT");
            GameObject fmtRow = CreateRow(col.transform);
            AddFormatButton(fmtRow.transform, "Classic 6 Love", MatchFormat.ClassicSixLove);
            AddFormatButton(fmtRow.transform, "Quick Love", MatchFormat.QuickLove);

            CreateSectionLabel(col.transform, "PLAYERS");
            GameObject sizeRow = CreateRow(col.transform);
            for (int n = 2; n <= 4; n++)
            {
                AddSizeButton(sizeRow.transform, n);
            }

            CreateRewardsCard(col.transform, "Winner takes the pot + 2,000 key bonus");

            GameObject start = CreateButton("Start", () => StartOnline(GameMode.CutThroat, _selectedSize, _selectedFormat));
            start.transform.SetParent(col.transform, worldPositionStays: false);
            return screen;
        }

        // ---- Partner screen -----------------------------------------------

        private GameObject BuildPartnerScreen()
        {
            GameObject screen = CreateScreen("PartnerScreen");
            CreateBackButton(screen.transform, ShowJamaicaMenu);
            CreateTitle(screen.transform, "Partner", -70f, 56f);

            GameObject col = CreateColumn(screen.transform);
            CreateSectionLabel(col.transform, "RANDOM 2 v 2 · 4 PLAYERS");
            CreateRewardsCard(col.transform, "Winning team takes the pot + key bonus");

            GameObject start = CreateButton("Find Match",
                () => StartOnline(GameMode.Partner, NetworkedMatch.MaxPlayers, MatchFormat.ClassicSixLove));
            start.transform.SetParent(col.transform, worldPositionStays: false);
            return screen;
        }

        // ---- One-Love With Friends (private create / join) ----------------

        private GameObject BuildFriendsScreen()
        {
            GameObject screen = CreateScreen("FriendsScreen");
            CreateBackButton(screen.transform, ShowJamaicaMenu);
            CreateTitle(screen.transform, "One-Love With Friends", -70f, 40f);

            GameObject col = CreateColumn(screen.transform);

            CreateSectionLabel(col.transform, "CREATE A ROOM");
            GameObject modeRow = CreateRow(col.transform);
            AddCreateModeButton(modeRow.transform, "Cut-Throat", GameMode.CutThroat);
            AddCreateModeButton(modeRow.transform, "Partner", GameMode.Partner);

            _friendsFormatRow = CreateRow(col.transform);
            AddFormatButton(_friendsFormatRow.transform, "Classic 6 Love", MatchFormat.ClassicSixLove);
            AddFormatButton(_friendsFormatRow.transform, "Quick Love", MatchFormat.QuickLove);

            _friendsSizeRow = CreateRow(col.transform);
            for (int n = 2; n <= 4; n++)
            {
                AddSizeButton(_friendsSizeRow.transform, n);
            }

            GameObject create = CreateButton("Create", OnCreateRoomClicked);
            create.transform.SetParent(col.transform, worldPositionStays: false);

            CreateSectionLabel(col.transform, "JOIN A ROOM");
            GameObject joinRow = CreateJoinRow(col.transform);
            joinRow.transform.SetParent(col.transform, worldPositionStays: false);

            return screen;
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

        // ---- Navigation ----------------------------------------------------

        private void ShowHub() => SetActiveScreen(_hubScreen);
        private void ShowJamaicaMenu() => SetActiveScreen(_jamaicaMenuScreen);
        private void ShowCutThroat() => SetActiveScreen(_cutThroatScreen);
        private void ShowPartner() => SetActiveScreen(_partnerScreen);
        private void ShowFriends() => SetActiveScreen(_friendsScreen);

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

        private void SetActiveScreen(GameObject? active)
        {
            _hubScreen?.SetActive(_hubScreen == active);
            _comingSoonScreen?.SetActive(_comingSoonScreen == active);
            _jamaicaMenuScreen?.SetActive(_jamaicaMenuScreen == active);
            _cutThroatScreen?.SetActive(_cutThroatScreen == active);
            _partnerScreen?.SetActive(_partnerScreen == active);
            _friendsScreen?.SetActive(_friendsScreen == active);
        }

        // ---- Matchmaking handlers (preserved) -----------------------------

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
                OnlineRoomActive?.Invoke(
                    PhotonBootstrap.Instance.CurrentRoomCode ?? string.Empty, size, mode, format);
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
            if (!_busy)
            {
                _waitingOverlay!.SetActive(false);
                return;
            }
            WaitingCancelled?.Invoke();
            _busy = false;
            _waitingOverlay!.SetActive(false);
        }

        /// <summary>Updates the waiting-room status (called by BoardBootstrap as seats fill).</summary>
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

        // ---- Coming soon ---------------------------------------------------

        private GameObject BuildComingSoon()
        {
            GameObject screen = CreateScreen("ComingSoonScreen");
            CreateBackButton(screen.transform, ShowHub);

            GameObject banner = new("Banner", typeof(RectTransform));
            banner.transform.SetParent(screen.transform, worldPositionStays: false);
            RectTransform bRt = (RectTransform)banner.transform;
            bRt.anchorMin = new Vector2(0.5f, 0.5f);
            bRt.anchorMax = new Vector2(0.5f, 0.5f);
            bRt.pivot = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = new Vector2(0f, 60f);
            bRt.sizeDelta = new Vector2(560f, 220f);
            _comingSoonHeader = banner.AddComponent<Image>();
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

        // ---- Waiting overlay ----------------------------------------------

        private GameObject BuildWaitingOverlay()
        {
            GameObject overlay = new("WaitingOverlay", typeof(RectTransform));
            overlay.transform.SetParent(transform, worldPositionStays: false);
            StretchFull((RectTransform)overlay.transform);
            Image dim = overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.8f);
            dim.raycastTarget = true; // block the screen behind while connecting

            GameObject status = new("Status", typeof(RectTransform));
            status.transform.SetParent(overlay.transform, worldPositionStays: false);
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
            _waitingStatus.text = string.Empty;
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

        private GameObject CreateScreen(string name)
        {
            GameObject screen = new(name, typeof(RectTransform));
            screen.transform.SetParent(transform, worldPositionStays: false);
            StretchFull((RectTransform)screen.transform);
            return screen;
        }

        private GameObject CreateGrid(Transform parent, Vector2 offset, Vector2 cell)
        {
            GameObject grid = new("Grid", typeof(RectTransform));
            grid.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = (RectTransform)grid.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            GridLayoutGroup glg = grid.AddComponent<GridLayoutGroup>();
            glg.cellSize = cell;
            glg.spacing = new Vector2(28f, 28f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.MiddleCenter;
            ContentSizeFitter fit = grid.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return grid;
        }

        private void CreateBlock(Transform parent, string name, string tag, Color[] colors, bool live, Action onClick)
        {
            GameObject card = new($"Block_{name}", typeof(RectTransform));
            card.transform.SetParent(parent, worldPositionStays: false);
            Image bg = card.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.14f, colors);
            bg.color = live ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.9f);
            AddShadow(card, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -6f));

            Button btn = card.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => onClick());

            GameObject foot = new("Foot", typeof(RectTransform));
            foot.transform.SetParent(card.transform, worldPositionStays: false);
            RectTransform footRt = (RectTransform)foot.transform;
            footRt.anchorMin = new Vector2(0f, 0f);
            footRt.anchorMax = new Vector2(1f, 0.6f);
            footRt.offsetMin = Vector2.zero;
            footRt.offsetMax = Vector2.zero;
            Image footImg = foot.AddComponent<Image>();
            footImg.sprite = GradientSprite.Vertical(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.72f));
            footImg.raycastTarget = false;

            GameObject nameGo = new("Name", typeof(RectTransform));
            nameGo.transform.SetParent(card.transform, worldPositionStays: false);
            RectTransform nameRt = (RectTransform)nameGo.transform;
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.anchoredPosition = new Vector2(0f, 54f);
            nameRt.sizeDelta = new Vector2(-24f, 74f);
            TextMeshProUGUI nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.alignment = TextAlignmentOptions.BottomLeft;
            nameTmp.fontSize = 32f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = Color.white;
            nameTmp.text = name;
            nameTmp.raycastTarget = false;
            nameTmp.margin = new Vector4(18f, 0f, 10f, 0f);

            GameObject tagGo = new("Tag", typeof(RectTransform));
            tagGo.transform.SetParent(card.transform, worldPositionStays: false);
            RectTransform tagRt = (RectTransform)tagGo.transform;
            tagRt.anchorMin = new Vector2(0f, 0f);
            tagRt.anchorMax = new Vector2(1f, 0f);
            tagRt.pivot = new Vector2(0.5f, 0f);
            tagRt.anchoredPosition = new Vector2(0f, 22f);
            tagRt.sizeDelta = new Vector2(-24f, 28f);
            TextMeshProUGUI tagTmp = tagGo.AddComponent<TextMeshProUGUI>();
            tagTmp.alignment = TextAlignmentOptions.BottomLeft;
            tagTmp.fontSize = 18f;
            tagTmp.color = live ? CodeTextColor : new Color(1f, 1f, 1f, 0.75f);
            tagTmp.text = tag;
            tagTmp.raycastTarget = false;
            tagTmp.margin = new Vector4(18f, 0f, 10f, 0f);
        }

        private GameObject CreateColumn(Transform parent)
        {
            GameObject col = new("Column", typeof(RectTransform));
            col.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = (RectTransform)col.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -20f);
            rt.sizeDelta = new Vector2(ButtonWidth + 60f, 720f);
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
            GameObject row = new("Row", typeof(RectTransform));
            row.transform.SetParent(parent, worldPositionStays: false);
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
            GameObject go = new("Section", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
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

        private void CreateRewardsCard(Transform parent, string rewardLine)
        {
            GameObject card = new("Rewards", typeof(RectTransform));
            card.transform.SetParent(parent, worldPositionStays: false);
            LayoutElement le = card.AddComponent<LayoutElement>();
            le.preferredWidth = ButtonWidth;
            le.preferredHeight = 140f;
            Image bg = card.AddComponent<Image>();
            bg.sprite = GradientSprite.RoundedDiagonal(0.14f, new Color(0.10f, 0.42f, 0.26f), new Color(0.03f, 0.16f, 0.10f));
            bg.color = Color.white;
            AddShadow(card, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -4f));

            GameObject text = new("Text", typeof(RectTransform));
            text.transform.SetParent(card.transform, worldPositionStays: false);
            StretchFull((RectTransform)text.transform);
            ((RectTransform)text.transform).offsetMin = new Vector2(16f, 12f);
            ((RectTransform)text.transform).offsetMax = new Vector2(-16f, -12f);
            TextMeshProUGUI tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 24f;
            tmp.color = BodyTextColor;
            tmp.text = $"<size=30><b>Entry <color=#FFD54A>1,000</color></b></size>\n{rewardLine}";
            tmp.raycastTarget = false;
        }

        private GameObject CreateJoinRow(Transform parent)
        {
            GameObject row = CreateRow(parent);

            GameObject inputGo = new("CodeInput", typeof(RectTransform));
            inputGo.transform.SetParent(row.transform, worldPositionStays: false);
            LayoutElement inputLe = inputGo.AddComponent<LayoutElement>();
            inputLe.preferredWidth = 280f;
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

            GameObject ph = new("Placeholder", typeof(RectTransform));
            ph.transform.SetParent(textArea.transform, worldPositionStays: false);
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
            GameObject go = new($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
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
            bg.sprite = GradientSprite.RoundedDiagonal(0.4f, new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.08f));
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

        private void CreateTitle(Transform parent, string text, float y, float size)
        {
            GameObject go = new("Title", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(1000f, 120f);
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

        private void CreateSubtitle(Transform parent, string text, float y)
        {
            GameObject go = new("Subtitle", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(1000f, 40f);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 26f;
            tmp.color = new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.85f);
            tmp.characterSpacing = 10f;
            tmp.text = text;
            tmp.raycastTarget = false;
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
