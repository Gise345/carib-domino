#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pose.Core;
using Pose.Core.Chat;
using Pose.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// M1 step 4 scene controller. Spatial table: Alice (human) at the bottom,
    /// Bob right, Cara top, Dan left, chain centred. Side seats render their
    /// hands as columns of landscape tiles; top + bottom render as rows of
    /// portrait tiles. Bots are <see cref="RandomBot"/> instances on a 1.5s
    /// timer.
    ///
    /// Tile interaction is per-tile: tiles with no meaningful end choice
    /// (single legal placement, OR both chain ends share the same pip)
    /// render in <b>Click</b> mode; tiles where the player must pick which
    /// end (matches both ends, ends differ) render in <b>Drag</b> mode and
    /// trigger the LEFT/RIGHT drop zones on the chain.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoardBootstrap : MonoBehaviour
    {
        private const ulong SpikeSeed = 0xC0FFEEUL;
        private const ulong BotSeed = SpikeSeed ^ 0xBADB07UL;

        private const float InitialBotPauseSeconds = 1.5f;
        private const float BotMoveDelaySeconds = 1.5f;
        private const float AutoPoseDelaySeconds = 3f;
        private const float AutoPassDelaySeconds = 3f;

        private static readonly Color FeltColor = new(0.05f, 0.30f, 0.18f, 1f);

        // Top hand holds opponent backs at the default tile size; the bottom
        // band has to fit the taller local tile.
        private const float TopBandHeight = 136f;
        private const float BottomBandHeight = 158f;
        private const float StatusFooterHeight = 90f;
        private const float SideBandWidth = 150f;
        private const float RegionPadding = 16f;

        // The top hand starts well down the screen because its profile now
        // sits BESIDE it rather than above it (BoardRoomHud.BuildSeat). Sharing
        // one band instead of stacking two is what returns ~66 units of height
        // to the chain area.
        // As high as the collapsed scoreboard allows: that panel ends at 162,
        // and this clears it by RegionPadding. Every unit reclaimed here goes
        // to the chain, which is what buys the extra tile per column.
        private const float TopRegionTopMargin = 178f;

        // The top hand centres on the board. Its profile lives at x 637–763, and
        // a full seven-tile hand spans 172–628, so centring still clears it.
        private const float TopRegionLeftInset = 76f;
        private const float TopRegionRightInset = 76f;

        // The local hand is bounded on the left by the turn clock and on the
        // right by the profile-and-chat corner, so it cannot simply centre.
        // 150..654 is the clear span between them; the fan is sized to fit it.
        private const float BottomHandLeftInset = 150f;
        private const float BottomHandRightInset = 146f;
        private const float BottomHandBottomOffset = 124f;

        // Centre-to-centre step for the local hand. A step larger than the tile
        // leaves a real gap; smaller would lap them.
        //
        // The tiles no longer overlap at all. The clear span between the turn
        // clock and the corner is 508 units, and seven tiles plus six gaps have
        // to live inside it — which caps the tile at 65 wide with an 8-unit
        // gap. Overlapping bought width, but a lapped hand does not look like
        // the reference: dominoes in a hand sit apart, and the covered edge ate
        // the outer pip column.
        private const float LocalHandFanStep = 73f;

        // Side hands start below their profiles, which dock at the top of each
        // column. Top-aligned rather than centred so this clearance holds on
        // short screens too, where a centred column would ride up into them.
        // A side profile widget is 128 tall centred on y 486 (it lost its name
        // plate), so it ends at 550; this clears it by RegionPadding.
        private const float SideRegionTopOffset = 628f;

        /// <summary>
        /// Sprites the dominoes are drawn from. Assign the TileArtSet asset in
        /// the Inspector; leave it empty and tiles draw themselves procedurally,
        /// exactly as they did before the art existed.
        /// </summary>
        [SerializeField] private TileArtSet? _tileArt;

        private static readonly PlayerId HumanPlayer = new("alice");

        private static readonly PlayerId[] Players =
        {
            HumanPlayer,
            new("bob"),
            new("cara"),
            new("dan"),
        };

        private readonly CutThroatRules _rules = new();
        private readonly RandomBot _bot = new();
        private readonly IRandomSource _botRng = new SeededRandomSource(BotSeed);

        private MatchState? _state;
        private ChainView? _chainView;
        private readonly Dictionary<PlayerId, HandView> _handViewByPlayer = new();
        private HandView? _bottomHandView;
        private HandView? _rightHandView;
        private HandView? _topHandView;
        private HandView? _leftHandView;
        private GameStatusView? _statusView;
        private EndOverlayView? _endOverlay;
        private Coroutine? _botRoutine;
        private bool _firstBotMove = true;

        // The player driving the local touch / drag input. Defaults to the
        // offline hot-seat constant; replaced with the online controller's
        // PlayerId once the networked deal lands. All input gating and
        // showBacks decisions consult this.
        private PlayerId _localPlayer = HumanPlayer;

        // True once the online room hands off control to the OnlineMatchController.
        // Suppresses the bot coroutine and routes input through RPCs instead of
        // applying moves locally.
        private bool _isOnline;

        // Latched when the online opponent leaves the Photon session. The
        // opponent-left overlay outranks the round-over overlay: there is
        // nobody left to rematch with, so the only offer is back-to-lobby.
        private bool _opponentLeft;

        // Set when this client was left alone in a round it can't settle
        // (the authority left); the overlay declares a local win.
        private bool _abandonedWin;

        // Set when the Cut-Throat series has been decided (M5).
        private bool _matchEnded;

        // Scoreboard HUD for a Cut-Throat series (top-left); null offline.
        private TextMeshProUGUI? _scoreboardText;
        private GameObject? _oldScoreboardPanel;

        // Cinematic board-room chrome (scoreboard, seat avatars, chat, action bar).
        private BoardRoomHud? _hud;
        private readonly Dictionary<PlayerId, SeatPosition> _seatPosByPlayer = new();

        // Between-rounds interstitial: shown once per round-over; a countdown
        // coroutine then owns its subtitle until the next round deals.
        private bool _seriesInterstitialShown;
        private Coroutine? _seriesCountdownRoutine;

        // "Who poses" side popup (pose rule): announces the round's opener when a
        // new series round deals. Auto-dismisses after PoserPopupSeconds; tapping
        // the close button dismisses it early.
        private const float PoserPopupSeconds = 10f;
        private GameObject? _poserPopup;
        private TextMeshProUGUI? _poserText;
        private Coroutine? _poserRoutine;

        // Bumped per offline "Play again" so each practice round deals a
        // different hand. Derived from SpikeSeed rather than a system RNG so
        // any given round remains reproducible from (SpikeSeed, index).
        private int _offlineRoundIndex;

        // Turn clock. This instance drives the on-screen countdown in BOTH modes
        // — practice and online — so every client sees the same pressure. It
        // also *enforces* the timeout offline, where this client owns the state.
        // Online, enforcement belongs to the table authority
        // (OnlineMatchController.TickTurnTimer): a client that is backgrounded
        // or force-quit stops ticking, and a stalled table must resolve anyway.
        private readonly TurnTimer _turnTimer = new();
        private TurnTimerView? _turnTimerView;
        private ShuffleAnimation? _shuffle;

        // The seat the clock is currently counting, so a turn change restarts
        // it. Null when nothing is being timed.
        private PlayerId? _timedPlayer;

        // Whether the timed turn is a forced pass, which runs a shorter window.
        // Tracked so the clock restarts if the situation changes mid-turn.
        private bool _timedForcedPass;

        // A tile the player tapped that can legally go on either end. Both ends
        // are lit while this is set, and the next end tapped plays it there.
        private TileView? _armedTile;

        private void Start()
        {
            // Before anything builds a tile. Null is fine — TileView falls back
            // to drawing itself, which is how the board looked before the art
            // landed.
            TileView.Art = _tileArt;

            GameSettings.Apply();

            ConfigureRoot();
            BuildSpatialLayout();
            BuildHomeButton();
            BuildEndOverlay();

            // Kick off Firebase init (auto-creates the singleton GameObject if
            // it doesn't already exist), then wait for sign-in before dealing.
            // The game logic itself doesn't need auth yet (M2.1) — we just want
            // to prove the boundary works end-to-end before profile/stats land.
            EnsureFirebaseBootstrap();

            FirebaseBootstrap fb = FirebaseBootstrap.Instance!;
            if (fb.IsReady)
            {
                OnFirebaseReady();
            }
            else if (fb.HasFailed)
            {
                OnFirebaseFailed(fb.ErrorMessage ?? "unknown error");
            }
            else
            {
                _statusView!.Setup(
                    L10n.Get("status_signing_in"),
                    passEnabled: false,
                    isOver: false);
                fb.Ready += OnFirebaseReady;
                fb.Failed += OnFirebaseFailed;
            }
        }

        private static void EnsureFirebaseBootstrap()
        {
            if (FirebaseBootstrap.Instance != null)
            {
                return;
            }
            GameObject go = new("FirebaseBootstrap");
            go.AddComponent<FirebaseBootstrap>();
        }

        private void OnFirebaseReady()
        {
            UnsubscribeFromFirebase();
            // A returning player already has a persisted session → straight to
            // profile + lobby. A new player has none → show the login screen,
            // which drives AuthService and calls back via OnLoggedIn (M7).
            if (AuthService.Instance != null && AuthService.Instance.IsSignedIn)
            {
                Debug.Log($"[BoardBootstrap] Session found, uid: {AuthService.Instance.Uid}");
                LoadProfile();
            }
            else
            {
                ShowLogin();
            }
        }

        private void OnFirebaseFailed(string error)
        {
            UnsubscribeFromFirebase();
            Debug.LogWarning($"[BoardBootstrap] Continuing offline — Firebase failed: {error}");
            // Fail-open: still let the player play. Stats/profile won't persist
            // this session, but the game loop is unaffected.
            StartGame();
        }

        private void UnsubscribeFromFirebase()
        {
            FirebaseBootstrap fb = FirebaseBootstrap.Instance!;
            fb.Ready -= OnFirebaseReady;
            fb.Failed -= OnFirebaseFailed;
        }

        // ---- Profile load (M2.2) -------------------------------------------

        private void LoadProfile()
        {
            _statusView!.Setup(
                L10n.Get("status_loading_profile"),
                passEnabled: false,
                isOver: false);

            EnsureProfileService();

            ProfileService ps = ProfileService.Instance!;
            if (ps.IsReady)
            {
                OnProfileReady();
            }
            else if (ps.HasFailed)
            {
                OnProfileFailed(ps.ErrorMessage ?? "unknown error");
            }
            else
            {
                // Subscribe BEFORE kicking off LoadOrCreate so we can't miss
                // the Ready/Failed event on a fast-completing path.
                ps.Ready += OnProfileReady;
                ps.Failed += OnProfileFailed;
                ps.LoadOrCreate(AuthService.Instance!.Uid!);
            }
        }

        private static void EnsureProfileService()
        {
            if (ProfileService.Instance != null)
            {
                return;
            }
            GameObject go = new("ProfileService");
            go.AddComponent<ProfileService>();
        }

        private void OnProfileReady()
        {
            UnsubscribeFromProfile();
            UserProfile profile = ProfileService.Instance!.Profile!;
            Debug.Log(
                $"[BoardBootstrap] Profile ready: \"{profile.DisplayName}\" " +
                $"({(ProfileService.Instance.IsNewProfile ? "new" : "returning")} player)");
            // Stats are settled server-side from the online round log (M4.3);
            // there is no client-side stats submission to set up here, and
            // offline practice no longer counts toward stats.
            // M3.2: Photon connection is no longer auto-started here. The
            // LobbyView (built in Start()) drives Create / Join when the
            // player chooses an online mode.
            ShowLobby();
        }

        // ---- Lobby (M3.2) + Online match (M3.3) --------------------------

        [SerializeField]
        [Tooltip("Drag the NetworkedMatch prefab here. The prefab must have a " +
                 "NetworkObject + NetworkedMatch component. M3.3 uses this to " +
                 "spawn the deal-state sync object on the host.")]
        private Fusion.NetworkObject? _networkedMatchPrefab;

        [SerializeField]
        [Tooltip("Drag the lobby background sprite (lobby-rum.png in " +
                 "Assets/images) here. Falls back to the felt-green panel " +
                 "color if left empty.")]
        private Sprite? _lobbyBackgroundSprite;

        [SerializeField]
        [Tooltip("Drag the board background sprite (board.png in " +
                 "Assets/images) here. Falls back to the felt-green color " +
                 "if left empty.")]
        private Sprite? _boardBackgroundSprite;

        [SerializeField]
        [Tooltip("Drag the Pose logo sprite (Pose-logo.png in Assets/images) " +
                 "here. Shown above the lobby's mode list.")]
        private Sprite? _logoSprite;

        [SerializeField]
        [Tooltip("Optional wooden button-frame sprite (1560x384, transparent, no " +
                 "text) drawn behind every lobby mode block. Leave empty to use " +
                 "the drawn wood/teal fallback.")]
        private Sprite? _modeButtonSprite;

        [SerializeField]
        [Tooltip("Painted art for the three game rooms — titles, format tiles " +
                 "and the rewards board. Every field is optional: a missing " +
                 "sprite draws a lettered stand-in, so art can land one file " +
                 "at a time. Supply transparent PNGs trimmed to their own bounds.")]
        private RoomArt _roomArt = new();

        [SerializeField]
        [Tooltip("Optional cursive TMP font asset for the 'Welcome to the Yard' " +
                 "lobby title. Create it from a cursive .ttf/.otf via " +
                 "Window > TextMeshPro > Font Asset Creator, then assign here.")]
        private TMPro.TMP_FontAsset? _titleFont;

        [SerializeField]
        [Tooltip("Drag the splash / loading sprite (posescreen.png in " +
                 "Assets/images) here. Shown during Firebase init before the " +
                 "lobby appears.")]
        private Sprite? _splashBackgroundSprite;

        private Image? _rootBackground;

        private LobbyView? _lobbyView;
        private LoginView? _loginView;
        private OnlineMatchController? _onlineMatchController;
        private Coroutine? _autoPoseRoutine;
        private Coroutine? _autoPassRoutine;

        // What the shared EndOverlayView is currently presenting, so its two
        // buttons dispatch to the right action.
        private enum OverlayMode { RoundOver, OpponentLeft, MatchOver }
        private OverlayMode _overlayMode = OverlayMode.RoundOver;

        private void ShowLogin()
        {
            if (_loginView != null)
            {
                return;
            }
            _hud?.gameObject.SetActive(false);
            GameObject go = new("LoginView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _loginView = go.AddComponent<LoginView>();
            _loginView.SetLogoSprite(_logoSprite);
            _loginView.SetBackgroundSprite(_lobbyBackgroundSprite);
            _loginView.LoggedIn += OnLoggedIn;
        }

        private void OnLoggedIn()
        {
            if (_loginView != null)
            {
                _loginView.LoggedIn -= OnLoggedIn;
                Destroy(_loginView.gameObject);
                _loginView = null;
            }
            LoadProfile();
        }

        private void ShowLobby()
        {
            if (_lobbyView != null)
            {
                return;
            }
            // The board-room HUD is a match-only view — hide it behind the lobby.
            _hud?.gameObject.SetActive(false);
            GameObject go = new("LobbyView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _lobbyView = go.AddComponent<LobbyView>();
            _lobbyView.SetLogoSprite(_logoSprite);
            _lobbyView.SetModeButtonSprite(_modeButtonSprite);
            _lobbyView.SetRoomArt(_roomArt);
            _lobbyView.SetTitleFont(_titleFont);
            _lobbyView.SetBackgroundSprite(_lobbyBackgroundSprite);
            _lobbyView.PracticeChosen += OnPracticeChosen;
            _lobbyView.OnlineRoomActive += OnOnlineRoomActive;
            _lobbyView.WaitingCancelled += OnWaitingCancelled;
            _lobbyView.LoggedOut += OnLoggedOut;
        }

        private void OnPracticeChosen()
        {
            UnsubscribeFromLobby();
            if (_lobbyView != null)
            {
                Destroy(_lobbyView.gameObject);
                _lobbyView = null;
            }
            // Offline mode — the existing bots-driven Cut-Throat scene.
            StartGame();
        }

        private void OnOnlineRoomActive(string roomCode, int playerCount, GameMode mode, MatchFormat format)
        {
            Debug.Log(
                $"[BoardBootstrap] Online room active: {roomCode} (count={playerCount}, mode={mode}, format={format}) — " +
                "starting OnlineMatchController");

            if (_networkedMatchPrefab == null)
            {
                Debug.LogError(
                    "[BoardBootstrap] NetworkedMatch prefab is not wired in the inspector. " +
                    "Drag the prefab onto the 'Networked Match Prefab' field on this " +
                    "GameObject and try again.");
                return;
            }
            if (PhotonBootstrap.Instance?.Runner == null)
            {
                Debug.LogError("[BoardBootstrap] PhotonBootstrap.Runner is null — cannot start online match.");
                return;
            }

            // Covers the startMatch round-trip for the server-issued seed.
            StartShuffle();

            string localPlayerId = ProfileService.Instance?.Profile?.DisplayName ?? "anon";
            string localUid = FirebaseBootstrap.Instance?.Uid ?? string.Empty;

            GameObject go = new("OnlineMatchController");
            _onlineMatchController = go.AddComponent<OnlineMatchController>();
            _onlineMatchController.MatchDealt += OnOnlineMatchDealt;
            _onlineMatchController.RoundStarted += OnOnlineRoundStarted;
            _onlineMatchController.MoveApplied += OnOnlineMoveApplied;
            _onlineMatchController.RematchVotesChanged += OnRematchVotesChanged;
            _onlineMatchController.WaitingChanged += OnWaitingChanged;
            _onlineMatchController.JoinFailed += OnJoinFailed;
            _onlineMatchController.OpponentLeft += OnOpponentLeft;
            _onlineMatchController.SeatsChanged += OnSeatsChanged;
            _onlineMatchController.MatchAbandonedWin += OnMatchAbandonedWin;
            _onlineMatchController.SeriesChanged += OnSeriesChanged;
            _onlineMatchController.MatchEnded += OnMatchEnded;
            _onlineMatchController.Setup(
                _networkedMatchPrefab,
                PhotonBootstrap.Instance.Runner,
                localPlayerId,
                localUid,
                playerCount,
                mode,
                format);

            // No fill timer, no "start now" prompt, no host: the table's authority
            // auto-deals when it fills or when its 60s deadline elapses (filling
            // empty seats with bots). See NetworkedMatch.AutoStartTimer (ADR 0011).

            // The lobby stays on screen showing the waiting status until the
            // deal lands — OnOnlineMatchDealt then destroys it and the board
            // takes over.
        }

        // ---- Online deal + move hooks (M3.4) ------------------------------

        private void OnOnlineMatchDealt(MatchState state)
        {
            _isOnline = true;
            _state = state;
            _localPlayer = _onlineMatchController!.LocalPlayer!.Value;
            SeatPlayersForOnline(state.Players, _localPlayer);
            ApplyRootSprite(_boardBackgroundSprite);

            // Lobby served its purpose; hand off to the board.
            if (_lobbyView != null)
            {
                UnsubscribeFromLobby();
                Destroy(_lobbyView.gameObject);
                _lobbyView = null;
            }

            Render();
            NotifyShuffleDealReady();
        }

        private void OnOnlineRoundStarted(MatchState state)
        {
            // A rematch was agreed and re-dealt. Seating and backgrounds are
            // already correct from the first deal; we just reset per-round
            // state and take the overlay down.
            StartShuffle();
            _state = state;
            _localPlayer = _onlineMatchController!.LocalPlayer!.Value;
            TileView.ClearSelection();
            _seriesInterstitialShown = false;
            if (_seriesCountdownRoutine != null)
            {
                StopCoroutine(_seriesCountdownRoutine);
                _seriesCountdownRoutine = null;
            }
            _endOverlay?.Hide();

            // Pose rule: announce the round's opener. The dealt state's current
            // player is the poser; FreeOpening marks the previous-winner free pose.
            AnnouncePoser(state);
            Render();
            NotifyShuffleDealReady();
        }

        // Shows the "who poses" popup at the start of a series round. Skipped for
        // non-series play (single rounds / practice keep the forced double-six).
        private void AnnouncePoser(MatchState state)
        {
            if (!_isOnline || _onlineMatchController == null || !_onlineMatchController.IsSeries)
            {
                return;
            }
            int seat = state.CurrentPlayerIndex;
            string name = _onlineMatchController.IsBotSeat(seat)
                ? L10n.Get("player_bot")
                : state.Players[seat].Value;
            ShowPoserPopup(name, state.FreeOpening);
        }

        private void OnOnlineMoveApplied(MatchState state, Move move)
        {
            _state = state;
            Render();
        }

        private void OnRematchVotesChanged()
        {
            // Re-render the overlay so a "your opponent wants a rematch" hint
            // (or our own "waiting…" state) reflects the latest votes.
            if (_endOverlay != null && _endOverlay.IsShowing)
            {
                Render();
            }
        }

        private void OnOpponentLeft()
        {
            _opponentLeft = true;
            Render();
        }

        // A player left and was replaced by a bot (3P/4P) — just re-render so
        // the vacated seat shows "Bot"; the round plays on.
        private void OnSeatsChanged()
        {
            Render();
        }

        // Everyone else left a round this client can't settle authoritatively —
        // end locally with a win (casual, no server stats; ADR 0011).
        private void OnMatchAbandonedWin()
        {
            _abandonedWin = true;
            _opponentLeft = true;
            Render();
        }

        // The series scores advanced (M5) — refresh the scoreboard.
        private void OnSeriesChanged()
        {
            UpdateScoreboard();
        }

        // The series was decided — show the match-over screen.
        private void OnMatchEnded()
        {
            _matchEnded = true;
            UpdateScoreboard();
            Render();
        }

        // ---- Pre-deal waiting (M3.7 / M3.9b auto-start) -------------------

        private void OnWaitingChanged()
        {
            // Reflect fill progress on the lobby while we wait for seats.
            if (_lobbyView == null || _onlineMatchController == null)
            {
                return;
            }
            int have = _onlineMatchController.RegisteredCount;
            int want = _onlineMatchController.TargetPlayerCount;
            _lobbyView.SetWaitingStatus(L10n.Get("waiting_for_players", have, want));
        }

        /// <summary>
        /// This client never got a seat at the table. Say so on the waiting
        /// overlay and hand the player back their Cancel button — the failure
        /// used to be silent, leaving them watching "waiting for players…"
        /// indefinitely while the others played on without them.
        /// </summary>
        private void OnJoinFailed(string reasonKey)
        {
            // The shuffle has long since run out its own ceiling by the time a
            // join is declared failed, so there is nothing to stop here.
            if (_lobbyView != null)
            {
                _lobbyView.FailWaiting(L10n.Get(reasonKey));
            }
        }

        private void UnsubscribeFromLobby()
        {
            if (_lobbyView == null)
            {
                return;
            }
            _lobbyView.PracticeChosen -= OnPracticeChosen;
            _lobbyView.OnlineRoomActive -= OnOnlineRoomActive;
            _lobbyView.WaitingCancelled -= OnWaitingCancelled;
            _lobbyView.LoggedOut -= OnLoggedOut;
        }

        private void OnLoggedOut()
        {
            // Tear down the lobby and return to the login screen (M7).
            UnsubscribeFromLobby();
            if (_lobbyView != null)
            {
                Destroy(_lobbyView.gameObject);
                _lobbyView = null;
            }
            ShowLogin();
        }

        private void OnProfileFailed(string error)
        {
            UnsubscribeFromProfile();
            Debug.LogWarning($"[BoardBootstrap] Continuing without profile: {error}");
            StartGame();
        }

        private void UnsubscribeFromProfile()
        {
            if (ProfileService.Instance == null)
            {
                return;
            }
            ProfileService.Instance.Ready -= OnProfileReady;
            ProfileService.Instance.Failed -= OnProfileFailed;
        }

        private void StartGame()
        {
            _isOnline = false;
            _localPlayer = HumanPlayer;
            _offlineRoundIndex = 0;
            SeatPlayersForOffline();
            ApplyRootSprite(_boardBackgroundSprite);

            DealOfflineRound();
        }

        /// <summary>
        /// Deals a fresh offline practice round. Called for the first deal and
        /// for each "Play again". Seating and background are already in place
        /// from <see cref="StartGame"/>, so this only re-deals and re-renders.
        /// </summary>
        private void DealOfflineRound()
        {
            StartShuffle();
            _firstBotMove = true;
            TileView.ClearSelection();
            _endOverlay?.Hide();

            ulong seed = SpikeSeed + (ulong)_offlineRoundIndex;
            _offlineRoundIndex++;

            _state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                Players,
                Partnership.CutThroat(Players),
                new SeededRandomSource(seed));

            Render();
            NotifyShuffleDealReady();
            ScheduleBotIfNeeded();
        }

        // ---- Click handler (unambiguous play) -----------------------------

        /// <summary>
        /// A tile that can go on either end has been tapped. Light both ends
        /// and remember it: the next tap on an end plays it there. Dragging the
        /// tile onto an end does the same thing by a different route.
        /// </summary>
        private void OnHumanTileSelected(TileView tv)
        {
            _armedTile = tv;
            OnHumanTileDragStarted(tv);
        }

        /// <summary>The armed tile was tapped again — stand down.</summary>
        private void OnHumanTileDeselected(TileView tv)
        {
            if (_armedTile == tv)
            {
                _armedTile = null;
            }
            HideDropZones();
        }

        /// <summary>
        /// An end was tapped. Plays the armed tile there — the half of the
        /// choose-an-end interaction that does not require dragging.
        /// </summary>
        private void OnEndTapped(ChainEnd end)
        {
            if (_armedTile == null || _state == null || _state.IsOver)
            {
                return;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                return;
            }

            Tile tile = _armedTile.Tile;
            _armedTile = null;
            HideDropZones();

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PlaceMove pm && pm.Tile == tile && pm.End == end)
                {
                    SubmitLocalMove(pm);
                    return;
                }
            }
        }

        private void HideDropZones()
        {
            if (_chainView == null)
            {
                return;
            }
            _chainView.LeftZone?.SetVisible(false);
            _chainView.RightZone?.SetVisible(false);
        }

        private void OnHumanTileClicked(TileView tv)
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                return;
            }

            // Apply the first matching legal placement. Click-mode tiles only
            // exist when there is no meaningful end choice — either the tile
            // has a single legal placement or both chain ends share the same
            // pip (so LEFT and RIGHT produce the same chain state).
            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PlaceMove pm && pm.Tile == tv.Tile)
                {
                    SubmitLocalMove(pm);
                    return;
                }
            }
        }

        // ---- Drag handlers (end choice required) --------------------------

        private void OnHumanTileDragStarted(TileView tv)
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }

            bool leftLegal = false;
            bool rightLegal = false;
            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PlaceMove pm && pm.Tile == tv.Tile)
                {
                    if (pm.End == ChainEnd.Left) leftLegal = true;
                    if (pm.End == ChainEnd.Right) rightLegal = true;
                }
            }

            string leftLabel = _state.Chain.IsEmpty
                ? string.Empty
                : _state.Chain.LeftEnd.ToString();
            string rightLabel = _state.Chain.IsEmpty
                ? string.Empty
                : _state.Chain.RightEnd.ToString();

            _chainView!.LeftZone!.SetVisible(leftLegal, leftLabel);
            _chainView.RightZone!.SetVisible(rightLegal, rightLabel);
        }

        private void OnHumanTileDragEnded(TileView tv)
        {
            if (_chainView != null)
            {
                _chainView.LeftZone?.SetVisible(false);
                _chainView.RightZone?.SetVisible(false);
            }
        }

        private void OnTileDroppedOnEnd(TileView tv, ChainEnd end)
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                return;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PlaceMove pm && pm.Tile == tv.Tile && pm.End == end)
                {
                    tv.NotifyDropAccepted();
                    SubmitLocalMove(pm);
                    return;
                }
            }
        }

        // ---- Pass button --------------------------------------------------

        private void OnPassClicked()
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                return;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PassMove pass)
                {
                    SubmitLocalMove(pass);
                    return;
                }
            }
        }

        /// <summary>
        /// Routes a local input through the right channel: offline applies
        /// immediately and re-renders; online sends an RPC and waits for the
        /// move to loop back through the networked log before the visual
        /// updates. The networked path's "tap → tile leaves your hand" feels
        /// like a brief beat because both clients only update once the host
        /// has appended; that's intentional (no client-side prediction).
        /// </summary>
        private void SubmitLocalMove(Move move)
        {
            if (_isOnline)
            {
                _onlineMatchController?.TrySubmitLocalMove(move);
                return;
            }
            _state = _rules.Apply(_state!, move);
            Render();
            ScheduleBotIfNeeded();
        }

        // ---- Bot loop -----------------------------------------------------

        private void ScheduleBotIfNeeded()
        {
            // Bots never run in online mode — the second player is a real
            // human reached via the networked move log.
            if (_isOnline)
            {
                return;
            }
            if (_state == null || _state.IsOver)
            {
                return;
            }
            if (_state.CurrentPlayer == _localPlayer)
            {
                return;
            }
            if (_botRoutine != null)
            {
                return;
            }
            _botRoutine = StartCoroutine(BotTurnRoutine());
        }

        private IEnumerator BotTurnRoutine()
        {
            while (_state != null && !_state.IsOver && _state.CurrentPlayer != _localPlayer)
            {
                float delay = _firstBotMove ? InitialBotPauseSeconds : BotMoveDelaySeconds;
                _firstBotMove = false;
                yield return new WaitForSeconds(delay);

                IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
                Move move = _bot.PickMove(_state, legal, _botRng);
                _state = _rules.Apply(_state, move);
                Render();
            }
            _botRoutine = null;
        }

        // ---- Turn clock ---------------------------------------------------

        private void Update()
        {
            TickTurnTimer();
        }

        /// <summary>
        /// Advances the on-screen turn clock and reacts to its thresholds.
        ///
        /// Runs in both modes. The countdown ring and the nudge are presentation
        /// and belong on every client; the auto-play is only submitted here when
        /// we are offline and therefore own the state. Online, the table
        /// authority submits it — see
        /// <c>OnlineMatchController.TickTurnTimer</c>. This client's clock can
        /// drift a little from the authority's, so the ring may hit zero a beat
        /// before or after the move actually lands; that's cosmetic.
        /// </summary>
        private void TickTurnTimer()
        {
            if (_turnTimerView == null)
            {
                return;
            }

            if (!ShouldTimeCurrentTurn())
            {
                if (_turnTimer.IsRunning)
                {
                    _turnTimer.Stop();
                    _turnTimerView.Hide();
                }
                _timedPlayer = null;
                return;
            }

            PlayerId current = _state!.CurrentPlayer;
            if (_timedPlayer == null || _timedPlayer.Value != current)
            {
                // A turn with nothing to decide gets a short window: staring at
                // a 30-second clock while the only legal move is "pass" is dead
                // time for everyone at the table. Evaluated once per turn, not
                // per frame — GetLegalMoves allocates, and the board cannot
                // change while a single player is on the clock.
                _timedPlayer = current;
                _timedForcedPass = IsForcedPass();
                _turnTimer.Restart(
                    _timedForcedPass ? AutoPassDelaySeconds : TurnTimer.ExpireAfterSeconds);
                _turnTimerView.ClearNudge();
            }

            TurnTimerEvent crossed = _turnTimer.Advance(Time.deltaTime);
            _turnTimerView.SetProgress(_turnTimer.Progress, _turnTimer.Remaining);

            switch (crossed)
            {
                case TurnTimerEvent.Nudged:
                    OnTurnNudge(current);
                    break;

                case TurnTimerEvent.Expired:
                    OnTurnExpired(current);
                    break;
            }
        }

        /// <summary>
        /// True when there is a live turn worth putting a clock on: a dealt,
        /// unfinished round with the board actually on screen. Between-rounds
        /// interstitials and the lobby are excluded so the ring doesn't count
        /// down over a screen the player can't act on.
        /// </summary>
        private bool ShouldTimeCurrentTurn()
        {
            if (_state == null || _state.IsOver || _matchEnded || _opponentLeft || _abandonedWin)
            {
                return false;
            }

            // The HUD is the match view; if it's hidden we're in the lobby.
            if (_hud == null || !_hud.gameObject.activeSelf)
            {
                return false;
            }

            // A bot seat plays on its own short cadence and can't be nudged.
            if (IsBotTurn())
            {
                return false;
            }

            return true;
        }

        private bool IsBotTurn()
        {
            if (!_isOnline)
            {
                // Offline, every seat but the local hot-seat player is a bot.
                return _state!.CurrentPlayer != _localPlayer;
            }

            if (_onlineMatchController == null)
            {
                return false;
            }
            return _onlineMatchController.IsBotSeat(_state!.CurrentPlayerIndex);
        }

        /// <summary>
        /// True when the current player's only legal move is to pass.
        /// </summary>
        private bool IsForcedPass()
        {
            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state!);
            return legal.Count == 1 && legal[0] is PassMove;
        }

        /// <summary>
        /// The player has stalled. Only the player actually on the clock is
        /// told — the banner sits in the middle of the board, and showing every
        /// client "waiting on Marlon" put a red bar across three people's
        /// boards for something none of them could act on. Their own countdown
        /// ring already shows who is holding things up.
        /// </summary>
        private void OnTurnNudge(PlayerId current)
        {
            if (current != _localPlayer)
            {
                return;
            }

            _turnTimerView!.ShowNudge(L10n.Get("nudge_your_turn"));

            // Deliberately unconditional — see Haptics.Nudge. A player who
            // could mute this would hold up the whole table silently.
            Haptics.Nudge();
        }

        /// <summary>
        /// The turn ran out. Offline we own the state, so play the tile now.
        /// Online the authority does it, and this client just stops counting and
        /// waits for the move to arrive through the networked log.
        /// </summary>
        private void OnTurnExpired(PlayerId current)
        {
            if (current == _localPlayer)
            {
                _turnTimerView!.ShowNudge(L10n.Get("nudge_auto_played"));
            }

            // A forced pass is already being submitted by the auto-pass
            // routine; the short clock only visualises its wait. Submitting
            // here too would race it.
            if (_isOnline || _timedForcedPass)
            {
                return;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state!);
            if (legal.Count == 0)
            {
                return;
            }

            SubmitLocalMove(AutoPlaySelector.Pick(legal));
        }

        private string SeatDisplayName(PlayerId player)
        {
            if (_isOnline
                && _onlineMatchController != null
                && _onlineMatchController.IsBotSeat(_state!.CurrentPlayerIndex))
            {
                return L10n.Get("player_bot");
            }
            return player.Value;
        }

        // ---- Auto-pose for the opening tile ------------------------------

        /// <summary>
        /// When the round opens and it's the local player's turn (chain empty,
        /// current player = local), kick a 3-second timer. If the player
        /// hasn't tapped the forced opening tile by then, auto-submit it so
        /// online play doesn't stall on an AFK host. Works the same offline
        /// (Alice's forced opening also auto-poses) and online.
        /// </summary>
        private void ScheduleAutoPoseIfNeeded()
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }
            if (!_state.Chain.IsEmpty)
            {
                return;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                return;
            }
            if (_autoPoseRoutine != null)
            {
                return;
            }
            _autoPoseRoutine = StartCoroutine(AutoPoseRoutine());
        }

        private IEnumerator AutoPoseRoutine()
        {
            yield return new WaitForSeconds(AutoPoseDelaySeconds);
            _autoPoseRoutine = null;

            // Re-check at fire time: the player may have tapped the tile in the
            // intervening 3 s. State changes invalidate the timer.
            if (_state == null || _state.IsOver)
            {
                yield break;
            }
            if (!_state.Chain.IsEmpty)
            {
                yield break;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                yield break;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PlaceMove openingMove)
                {
                    Debug.Log("[BoardBootstrap] auto-posing opening tile");
                    SubmitLocalMove(openingMove);
                    yield break;
                }
            }
        }

        // ---- Auto-pass when no tile can be played -------------------------

        /// <summary>
        /// When the local player's only legal action is a pass (no tile in
        /// hand matches either open end), kick a 3-second timer. If the
        /// player hasn't tapped the Pass button by then, auto-submit so play
        /// doesn't stall waiting on an obvious forced move.
        /// </summary>
        private void ScheduleAutoPassIfNeeded()
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                return;
            }
            if (_autoPassRoutine != null)
            {
                return;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            bool onlyPass = legal.Count == 1 && legal[0] is PassMove;
            if (!onlyPass)
            {
                return;
            }

            _autoPassRoutine = StartCoroutine(AutoPassRoutine());
        }

        private IEnumerator AutoPassRoutine()
        {
            yield return new WaitForSeconds(AutoPassDelaySeconds);
            _autoPassRoutine = null;

            if (_state == null || _state.IsOver)
            {
                yield break;
            }
            if (_state.CurrentPlayer != _localPlayer)
            {
                yield break;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PassMove autoPass)
                {
                    Debug.Log("[BoardBootstrap] auto-passing");
                    SubmitLocalMove(autoPass);
                    yield break;
                }
            }
        }

        // ---- Render -------------------------------------------------------

        private void Render()
        {
            MatchState state = _state!;
            Tile? openingTile = null;
            if (state.History.Count > 0 && state.History[0] is PlaceMove openingMove)
            {
                openingTile = openingMove.Tile;
            }
            _chainView!.Setup(state.Chain, openingTile);

            // Hands are rebuilt below, so any armed tile is about to be
            // destroyed. Drop the reference and put the end lights out.
            _armedTile = null;
            HideDropZones();

            // Per-tile interaction mode for the local player's hand.
            //
            // For each playable tile we count its legal placements: 1 → Click
            // (no choice), 2 → Drag if the two open ends differ (true choice),
            // 2 → Click if the two open ends share a pip (no functional
            // difference between LEFT and RIGHT, so don't make the player drag).
            Dictionary<Tile, TileInteractionMode> tileModes = new();
            bool currentPlayerHasPass = false;

            if (!state.IsOver)
            {
                IReadOnlyList<Move> legal = _rules.GetLegalMoves(state);
                Dictionary<Tile, int> placementCount = new();
                foreach (Move m in legal)
                {
                    switch (m)
                    {
                        case PlaceMove pm:
                            placementCount.TryGetValue(pm.Tile, out int count);
                            placementCount[pm.Tile] = count + 1;
                            break;
                        case PassMove:
                            currentPlayerHasPass = true;
                            break;
                    }
                }

                bool endsDiffer = !state.Chain.IsEmpty
                    && state.Chain.LeftEnd != state.Chain.RightEnd;

                foreach (KeyValuePair<Tile, int> kv in placementCount)
                {
                    bool requiresChoice = kv.Value == 2 && endsDiffer;
                    tileModes[kv.Key] = requiresChoice
                        ? TileInteractionMode.Drag
                        : TileInteractionMode.Click;
                }
            }

            bool isLocalTurn = !state.IsOver && state.CurrentPlayer == _localPlayer;

            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (!_handViewByPlayer.TryGetValue(p, out HandView? hv))
                {
                    // No seat assigned (shouldn't happen for valid configs, but
                    // guards against future mode-mismatch bugs).
                    continue;
                }
                bool isCurrent = !state.IsOver && p == state.CurrentPlayer;
                bool isLocal = p == _localPlayer;
                Func<Tile, TileInteractionMode>? predicate =
                    (isLocalTurn && isLocal)
                        ? new Func<Tile, TileInteractionMode>(tile =>
                            tileModes.TryGetValue(tile, out TileInteractionMode m)
                                ? m
                                : TileInteractionMode.None)
                        : null;
                // Nobody sees anyone else's tiles — only how many they hold.
                // This used to be gated on being online, from when practice was
                // a hot-seat game passed between people on one device. Practice
                // is against bots now, so that only leaked their hands.
                bool showBacks = !isLocal;
                // Team games tint each name-plate by team (local team vs the
                // opposing team); Cut-Throat clears back to white.
                hv.SetAccentColor(TeamAccentColor(state, p));
                // A seat whose player left is played out by a bot (M3.9b) — label it.
                string displayName = (_isOnline && _onlineMatchController != null && _onlineMatchController.IsBotSeat(i))
                    ? L10n.Get("player_bot")
                    : p.Value;
                hv.Setup(displayName, isCurrent, state.Hands[p], predicate, showBacks);
            }

            _statusView!.Setup(
                FormatStatus(state, isLocalTurn),
                passEnabled: isLocalTurn && currentPlayerHasPass,
                isOver: state.IsOver);

            // Stats are settled server-side (M4.3): the online host submits the
            // round log to submitRoundLog, which replays it and writes the
            // recomputed result. There is no client-side stats write here, and
            // offline practice deliberately does not count toward stats.

            UpdateScoreboard();
            RefreshHud(state, isLocalTurn, currentPlayerHasPass);

            // Present (or dismiss) the end-of-round / opponent-left overlay.
            // Kept after the status/hand render so the board behind the
            // dimmed backdrop shows the final position.
            RefreshEndOverlay(state);

            // The opening tile is forced (single legal move). Give the local
            // player 3 seconds to ritualistically tap it, then auto-pose so
            // online play doesn't stall on an AFK host. Idempotent — the
            // routine guards against double-scheduling.
            ScheduleAutoPoseIfNeeded();
            // Mirror behaviour for forced passes (no playable tile): 3 s
            // grace, then auto-submit. The Pass button itself remains 1-tap
            // since pass is only legal when there's no alternative anyway.
            ScheduleAutoPassIfNeeded();
        }

        private string FormatStatus(MatchState state, bool isLocalTurn)
        {
            if (state.IsOver)
            {
                MatchOutcome? outcome = _rules.GetOutcome(state);
                if (outcome != null)
                {
                    return FormatOutcome(outcome, state);
                }
            }

            return isLocalTurn
                ? L10n.Get("status_your_turn", state.CurrentPlayer.Value)
                : L10n.Get("status_waiting_for", state.CurrentPlayer.Value);
        }

        private string FormatOutcome(MatchOutcome outcome, MatchState state)
        {
            string reasonKey = outcome.Reason switch
            {
                MatchEndReason.Domino => "end_reason_domino",
                MatchEndReason.Blocked => "end_reason_block",
                MatchEndReason.Resigned => "end_reason_resigned",
                _ => "end_reason_domino",
            };
            string reason = L10n.Get(reasonKey);

            if (outcome.IsDraw)
            {
                return L10n.Get("status_round_over_draw", reason);
            }

            // Partner games (a team with more than one member) frame the result
            // from the local player's team perspective rather than naming a single
            // winner. Cut-Throat (solo teams) keeps the individual-winner text.
            if (IsTeamGame(state) && outcome.WinningTeamId != null)
            {
                bool localWon = state.Partnership.GetTeamOf(_localPlayer) == outcome.WinningTeamId.Value;
                return L10n.Get(
                    localWon ? "status_round_over_team_win" : "status_round_over_team_loss",
                    reason,
                    outcome.WinnerScore);
            }

            return L10n.Get(
                "status_round_over_winner",
                reason,
                outcome.WinnerId!.Value.Value,
                outcome.WinnerScore);
        }

        private static bool IsTeamGame(MatchState state)
        {
            foreach (Team team in state.Partnership.Teams)
            {
                if (team.Members.Count > 1)
                {
                    return true;
                }
            }
            return false;
        }

        // Fixed name-plate tints per team, so every device shows the SAME colours:
        // team A (seats 0 & 2) reads blue, team B (seats 1 & 3) reads gold. Cut-
        // Throat has no teams to distinguish, so it stays plain white.
        private static readonly Color TeamColorA = new(0.35f, 0.60f, 1.0f);
        private static readonly Color TeamColorB = new(1.0f, 0.85f, 0.35f);

        private static Color TeamAccentColor(MatchState state, PlayerId player)
        {
            if (!IsTeamGame(state))
            {
                return Color.white;
            }
            // Colour by the team's identity, not the local player's perspective —
            // otherwise each device paints its own team the same colour and the
            // teams are indistinguishable across devices.
            System.Collections.Generic.IReadOnlyList<Team> teams = state.Partnership.Teams;
            TeamId team = state.Partnership.GetTeamOf(player);
            return teams.Count > 0 && team == teams[0].Id ? TeamColorA : TeamColorB;
        }

        // ---- Layout scaffolding -------------------------------------------

        private void ConfigureRoot()
        {
            RectTransform rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _rootBackground = gameObject.AddComponent<Image>();
            ApplyRootSprite(_splashBackgroundSprite);
        }

        /// <summary>
        /// Swap the root background sprite. Used to transition from the splash
        /// art (visible during Firebase auth + profile load) to the gameplay
        /// board art once a round is about to start.
        /// </summary>
        private void ApplyRootSprite(Sprite? sprite)
        {
            if (_rootBackground == null)
            {
                return;
            }
            if (sprite != null)
            {
                _rootBackground.sprite = sprite;
                _rootBackground.color = Color.white;
                _rootBackground.type = Image.Type.Simple;
                _rootBackground.preserveAspect = false;
            }
            else
            {
                _rootBackground.sprite = null;
                _rootBackground.color = FeltColor;
            }
        }

        private void BuildSpatialLayout()
        {
            // Cinematic vignette: darkens the corners over the felt for depth.
            // Added first so it sits above the board background but behind every
            // seat, tile and the chain; never intercepts input.
            CreateVignette();
            CreateScoreboard();
            CreatePoserPopup();

            // Where the top cluster ends and the side columns / chain begin.
            float topClusterBottom = TopRegionTopMargin + TopBandHeight;
            // Where the bottom cluster starts, measured up from the bottom edge.
            float bottomClusterTop = BottomHandBottomOffset + BottomBandHeight;

            RectTransform topRegion = CreateRegion(
                "TopRegion",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(TopRegionLeftInset, -topClusterBottom),
                offsetMax: new Vector2(-TopRegionRightInset, -TopRegionTopMargin));
            ConfigureRegionAsCenteredRow(topRegion);

            RectTransform bottomRegion = CreateRegion(
                "BottomRegion",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0f),
                offsetMin: new Vector2(BottomHandLeftInset, BottomHandBottomOffset),
                offsetMax: new Vector2(-BottomHandRightInset, bottomClusterTop));
            ConfigureRegionAsVerticalStack(bottomRegion);

            RectTransform leftRegion = CreateRegion(
                "LeftRegion",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 1f),
                offsetMin: new Vector2(0f, bottomClusterTop + RegionPadding),
                offsetMax: new Vector2(SideBandWidth, -SideRegionTopOffset));
            ConfigureRegionAsColumn(leftRegion, TextAnchor.UpperCenter);

            RectTransform rightRegion = CreateRegion(
                "RightRegion",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-SideBandWidth, bottomClusterTop + RegionPadding),
                offsetMax: new Vector2(0f, -SideRegionTopOffset));
            ConfigureRegionAsColumn(rightRegion, TextAnchor.UpperCenter);

            RectTransform centerRegion = CreateRegion(
                "CenterRegion",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(SideBandWidth + RegionPadding, bottomClusterTop + RegionPadding),
                offsetMax: new Vector2(-(SideBandWidth + RegionPadding), -(topClusterBottom + RegionPadding)));
            ConfigureRegionAsCenteredRow(centerRegion);

            _chainView = CreateChainViewInside(centerRegion);

            // Seats are built once at scene load. Player-to-seat binding (and
            // whether a seat is even used) is decided per-round when the deal
            // lands — see SeatPlayersForOffline / SeatPlayersForOnline.
            // Only the bottom seat gets the larger, spaced tiles — it is the one
            // hand whose faces you read and aim at. The other three render as
            // backs, so extra size would buy nothing and cost the side columns
            // width they do not have.
            _bottomHandView = CreateHandView(
                Players[0], bottomRegion, HandOrientation.Horizontal, TileOrientation.Portrait,
                includesStatus: true,
                shortDim: TileView.LocalShortDim,
                longDim: TileView.LocalLongDim,
                fanStep: LocalHandFanStep,
                showName: false);
            // No inline name plates on any seat: every seat now carries a
            // profile widget with its name (BoardRoomHud.SetSeat, which runs
            // offline as well as online), so a second label is duplication —
            // and on the top row it is duplication the band cannot afford. The
            // hand is 456 wide in a 488-wide region; a 120-wide plate beside it
            // would overflow.
            _rightHandView = CreateHandView(
                Players[1], rightRegion, HandOrientation.Vertical, TileOrientation.Landscape,
                includesStatus: false, showName: false);
            _topHandView = CreateHandView(
                Players[2], topRegion, HandOrientation.Horizontal, TileOrientation.Portrait,
                includesStatus: false, showName: false);
            _leftHandView = CreateHandView(
                Players[3], leftRegion, HandOrientation.Vertical, TileOrientation.Landscape,
                includesStatus: false, showName: false);

            CreateBoardRoomHud();
            CreateChatPanel();
            CreateTurnTimerView();
            CreateShuffleAnimation();
        }

        /// <summary>
        /// The opening shuffle. Built last so it covers everything — it is a
        /// full-screen scrim, and the board is allowed to render underneath it
        /// as normal. That is what keeps the deal off the animation's critical
        /// path: nothing waits, the finished board is simply revealed as the
        /// tiles are dealt away.
        /// </summary>
        private void CreateShuffleAnimation()
        {
            GameObject go = new("ShuffleAnimation", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _shuffle = go.AddComponent<ShuffleAnimation>();
        }

        /// <summary>
        /// Starts the shuffle as a match begins, before the deal is asked for.
        /// Once per match, not per round — a series already has its own
        /// between-rounds interstitial, and reshuffling on every round would
        /// sit in front of players who are mid-game.
        /// </summary>
        private void StartShuffle()
        {
            if (_shuffle == null)
            {
                return;
            }
            // Hands stay empty until the shuffle actually deals them out. The
            // board still renders behind the scrim as before — this only holds
            // the tiles back, so they arrive with the flying ones rather than
            // being revealed already sitting in place when the scrim fades.
            SetHandsVisible(false);
            _shuffle.Play(onComplete: () => SetHandsVisible(true));
        }

        /// <summary>
        /// Fades the four hands in or out. A CanvasGroup rather than
        /// deactivating them, so their layout keeps resolving while hidden and
        /// the tiles are already in place the instant they are shown.
        /// </summary>
        private void SetHandsVisible(bool visible)
        {
            HandView?[] hands = { _bottomHandView, _rightHandView, _topHandView, _leftHandView };
            foreach (HandView? hv in hands)
            {
                if (hv == null)
                {
                    continue;
                }
                if (!hv.TryGetComponent(out CanvasGroup group))
                {
                    group = hv.gameObject.AddComponent<CanvasGroup>();
                }
                group.alpha = visible ? 1f : 0f;
                group.blocksRaycasts = visible;
            }
        }

        /// <summary>
        /// The deal has landed and the board has rendered behind the scrim.
        /// Lets the shuffle wind up at its next cycle boundary.
        /// </summary>
        private void NotifyShuffleDealReady()
        {
            _shuffle?.NotifyDealReady();
        }

        /// <summary>
        /// The turn clock overlay. Built last so it draws above the board-room
        /// HUD — the countdown has to stay readable over the felt and the
        /// action bar. <see cref="TurnTimerView"/> docks itself bottom-left
        /// above Last Play, so there is nothing to position here. It hides
        /// itself on Awake and only appears once a live turn is being timed.
        /// </summary>
        private void CreateTurnTimerView()
        {
            GameObject go = new("TurnTimerView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _turnTimerView = go.AddComponent<TurnTimerView>();
        }

        // Builds the cinematic board-room chrome as a full-screen overlay and
        // wires its buttons. Supersedes the plain scoreboard + status footer,
        // which are hidden (kept only so existing Render calls stay valid).
        private void CreateBoardRoomHud()
        {
            GameObject go = new("BoardRoomHud", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            // Hide FIRST — the HUD is a match-only view (shown from RefreshHud).
            // Doing this before we build/wire it guarantees it never flashes on
            // the splash/lobby even if something below throws.
            go.SetActive(false);
            _hud = go.AddComponent<BoardRoomHud>();
            _hud.Init();
            _hud.PassClicked += OnPassClicked;
            _hud.HomeClicked += OnHomePressed;
            _hud.SettingsClicked += OnHomePressed;
            _hud.ChatClicked += OnChatPressed;

            if (_oldScoreboardPanel != null)
            {
                _oldScoreboardPanel.SetActive(false);
            }
            if (_statusView != null)
            {
                // Keep the old status footer as an INVISIBLE spacer, not disabled:
                // it sits in the bottom region's vertical stack, so removing it
                // would let the local hand drop down onto the HUD's Pass button.
                // Reserve enough height that the hand clears the action bar.
                if (!_statusView.TryGetComponent(out CanvasGroup cg))
                {
                    cg = _statusView.gameObject.AddComponent<CanvasGroup>();
                }
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
                LayoutElement le = _statusView.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = 120f;
                    le.minHeight = 120f;
                }
            }
        }

        private void CreateVignette()
        {
            GameObject go = new("Vignette", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.AddComponent<Image>();
            img.sprite = GradientSprite.Radial(
                new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.62f), clearFraction: 0.4f);
            img.color = Color.white;
            img.raycastTarget = false;
        }

        private void CreateScoreboard()
        {
            GameObject go = new("Scoreboard", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f, -20f);
            rt.sizeDelta = new Vector2(300f, 200f);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.42f);
            bg.raycastTarget = false;

            GameObject textGo = new("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 10f);
            trt.offsetMax = new Vector2(-14f, -10f);
            _scoreboardText = textGo.AddComponent<TextMeshProUGUI>();
            _scoreboardText.alignment = TextAlignmentOptions.TopLeft;
            _scoreboardText.fontSize = 22f;
            _scoreboardText.color = new Color(0.97f, 0.95f, 0.88f);
            _scoreboardText.raycastTarget = false;
            _scoreboardText.text = string.Empty;

            go.SetActive(false); // superseded by the BoardRoomHud scoreboard
            _oldScoreboardPanel = go;
        }

        // Builds the "who poses" side popup: a panel anchored to the right edge,
        // vertically centred, with a message and a close button. Hidden until a
        // series round announces its opener (see ShowPoserPopup).
        private void CreatePoserPopup()
        {
            GameObject go = new("PoserPopup", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-16f, 0f);
            rt.sizeDelta = new Vector2(300f, 132f);

            Image bg = go.AddComponent<Image>();
            bg.sprite = GradientSprite.Vertical(
                new Color(0.10f, 0.36f, 0.20f), new Color(0.05f, 0.20f, 0.11f));
            bg.color = Color.white;

            GameObject textGo = new("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 44f);
            trt.offsetMax = new Vector2(-16f, -14f);
            _poserText = textGo.AddComponent<TextMeshProUGUI>();
            _poserText.alignment = TextAlignmentOptions.Center;
            _poserText.fontSize = 24f;
            _poserText.color = new Color(0.98f, 0.96f, 0.9f);
            _poserText.raycastTarget = false;
            _poserText.text = string.Empty;

            GameObject btnGo = new("Close", typeof(RectTransform));
            btnGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform brt = (RectTransform)btnGo.transform;
            brt.anchorMin = new Vector2(0.5f, 0f);
            brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 12f);
            brt.sizeDelta = new Vector2(160f, 40f);
            Image btnBg = btnGo.AddComponent<Image>();
            btnBg.color = new Color(1f, 1f, 1f, 0.16f);
            Button btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(HidePoserPopup);

            GameObject btnLabelGo = new("Label", typeof(RectTransform));
            btnLabelGo.transform.SetParent(btnGo.transform, worldPositionStays: false);
            RectTransform lrt = (RectTransform)btnLabelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            TextMeshProUGUI btnLabel = btnLabelGo.AddComponent<TextMeshProUGUI>();
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.fontSize = 20f;
            btnLabel.color = new Color(0.98f, 0.96f, 0.9f);
            btnLabel.text = L10n.Get("btn_close");

            go.SetActive(false);
            _poserPopup = go;
        }

        // Announces who opens the round. `free` distinguishes a free pose (the
        // previous winner may lead any tile) from a forced open (highest double).
        private void ShowPoserPopup(string poserName, bool free)
        {
            if (_poserPopup == null || _poserText == null)
            {
                return;
            }
            _poserText.text = free
                ? L10n.Get("poser_announce_free", poserName)
                : L10n.Get("poser_announce", poserName);
            _poserPopup.SetActive(true);
            if (_poserRoutine != null)
            {
                StopCoroutine(_poserRoutine);
            }
            _poserRoutine = StartCoroutine(PoserPopupRoutine());
        }

        private IEnumerator PoserPopupRoutine()
        {
            yield return new WaitForSeconds(PoserPopupSeconds);
            _poserRoutine = null;
            HidePoserPopup();
        }

        private void HidePoserPopup()
        {
            if (_poserRoutine != null)
            {
                StopCoroutine(_poserRoutine);
                _poserRoutine = null;
            }
            _poserPopup?.SetActive(false);
        }

        /// <summary>
        /// Feeds the board-room scoreboard: subtitle (target), round, tiles on
        /// board, and each seat's games + points. In a series those come from the
        /// networked series totals (each seat shows its TEAM's totals — for
        /// Cut-Throat solo teams that's the player; for Partner both partners show
        /// the shared team total). Offline / non-series shows names with zeros.
        /// </summary>
        private void UpdateScoreboard()
        {
            if (_hud == null || _state == null)
            {
                return;
            }
            OnlineMatchController? c = _onlineMatchController;
            bool series = _isOnline && c != null && c.IsSeries;

            MatchFormat format = series ? c!.SeriesFormat : MatchFormat.ClassicSixLove;
            int loves = MatchFormatRules.For(format).TargetPoints / MatchFormatRules.PointsPerRoundWin;
            _hud.SetScoreHeader(
                L10n.Get("scoreboard_sub", loves),
                series ? c!.SeriesRoundNumber : 1,
                _state.Chain.Count);

            for (int i = 0; i < 4; i++)
            {
                if (i >= _state.Players.Count)
                {
                    _hud.SetScoreRow(i, false, string.Empty, Color.white, 0, 0);
                    continue;
                }
                bool bot = series && c!.IsBotSeat(i);
                string name = bot ? L10n.Get("player_bot") : _state.Players[i].Value;
                int games = series ? c!.SeriesGamesForSeat(i) : 0;
                int points = series ? c!.SeriesPointsForSeat(i) : 0;
                _hud.SetScoreRow(i, true, name, BoardRoomHud.SeatColors[i % 4], games, points);
            }
        }

        // Pushes per-seat avatars, the turn tag / Pass state, and the last-play
        // tile to the board-room HUD. Colours are stable per seat index.
        private void RefreshHud(MatchState state, bool isLocalTurn, bool currentPlayerHasPass)
        {
            if (_hud == null)
            {
                return;
            }
            // A round is being rendered → the board-room HUD is now the active view.
            _hud.gameObject.SetActive(true);
            OnlineMatchController? c = _onlineMatchController;

            HashSet<SeatPosition> used = new();
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (!_seatPosByPlayer.TryGetValue(p, out SeatPosition pos))
                {
                    continue;
                }
                used.Add(pos);
                bool bot = _isOnline && c != null && c.IsBotSeat(i);
                bool isCurrent = !state.IsOver && p == state.CurrentPlayer;
                string name = bot ? L10n.Get("player_bot") : p.Value;
                // The pill counts tiles still in hand — the number that matters
                // while a round is live. Series scores live on the scoreboard.
                int tilesLeft = state.Hands.TryGetValue(p, out Hand h) ? h.Count : 0;
                // Team games colour the seat by TEAM, not by seat index. The
                // hand's name plate used to carry this tint, but the plates are
                // gone now that every seat has a profile — so the signal has to
                // live on the profile or Partner mode loses it entirely.
                // TeamAccentColor returns white outside team games, which is
                // not a usable disc colour, so fall back to the seat palette.
                Color teamTint = TeamAccentColor(state, p);
                Color seatColor = teamTint == Color.white
                    ? BoardRoomHud.SeatColors[i % 4]
                    : teamTint;
                _hud.SetSeat(pos, true, name, seatColor, tilesLeft, online: !bot, currentTurn: isCurrent);
            }

            // Hide any avatar slot not used by this table's player count.
            foreach (SeatPosition pos in _allSeatPositions)
            {
                if (!used.Contains(pos))
                {
                    _hud.SetSeat(pos, false, string.Empty, Color.white, 0, false, false);
                }
            }

            _hud.SetTurn(isLocalTurn, mustPass: isLocalTurn && currentPlayerHasPass);

            PlaceMove? lastPlace = null;
            for (int i = state.History.Count - 1; i >= 0; i--)
            {
                if (state.History[i] is PlaceMove pm)
                {
                    lastPlace = pm;
                    break;
                }
            }
            if (lastPlace != null)
            {
                _hud.SetLastPlay(true, lastPlace.Tile.A, lastPlace.Tile.B);
            }
            else
            {
                _hud.SetLastPlay(false, 0, 0);
            }
        }

        private static readonly SeatPosition[] _allSeatPositions =
        {
            SeatPosition.Bottom, SeatPosition.Right, SeatPosition.Top, SeatPosition.Left,
        };

        // ---- Seat binding (per-round) -------------------------------------

        /// <summary>
        /// Offline 4P: hardcoded seat order around the table —
        /// alice→bottom, bob→right, cara→top, dan→left. All four seats active.
        /// </summary>
        private void SeatPlayersForOffline()
        {
            _handViewByPlayer.Clear();
            _handViewByPlayer[Players[0]] = _bottomHandView!;
            _handViewByPlayer[Players[1]] = _rightHandView!;
            _handViewByPlayer[Players[2]] = _topHandView!;
            _handViewByPlayer[Players[3]] = _leftHandView!;
            _seatPosByPlayer.Clear();
            _seatPosByPlayer[Players[0]] = SeatPosition.Bottom;
            _seatPosByPlayer[Players[1]] = SeatPosition.Right;
            _seatPosByPlayer[Players[2]] = SeatPosition.Top;
            _seatPosByPlayer[Players[3]] = SeatPosition.Left;
            _bottomHandView!.gameObject.SetActive(true);
            _rightHandView!.gameObject.SetActive(true);
            _topHandView!.gameObject.SetActive(true);
            _leftHandView!.gameObject.SetActive(true);
        }

        /// <summary>
        /// Online 2–4P: the local player sits at the bottom and the others are
        /// placed around the table in turn order via
        /// <see cref="SeatArrangement"/>. Unused seats are hidden. Symmetric —
        /// each device sees itself below regardless of who hosted.
        /// </summary>
        private void SeatPlayersForOnline(IReadOnlyList<PlayerId> players, PlayerId local)
        {
            _handViewByPlayer.Clear();

            int localIndex = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Equals(local))
                {
                    localIndex = i;
                    break;
                }
            }

            SeatPosition[] seats = SeatArrangement.Arrange(players.Count, localIndex);

            // Start all seats hidden; activate the ones we use.
            _bottomHandView!.gameObject.SetActive(false);
            _rightHandView!.gameObject.SetActive(false);
            _topHandView!.gameObject.SetActive(false);
            _leftHandView!.gameObject.SetActive(false);

            _seatPosByPlayer.Clear();
            for (int i = 0; i < players.Count; i++)
            {
                HandView seat = HandViewForSeat(seats[i]);
                _handViewByPlayer[players[i]] = seat;
                _seatPosByPlayer[players[i]] = seats[i];
                seat.gameObject.SetActive(true);
            }
        }

        private HandView HandViewForSeat(SeatPosition seat) => seat switch
        {
            SeatPosition.Bottom => _bottomHandView!,
            SeatPosition.Right => _rightHandView!,
            SeatPosition.Top => _topHandView!,
            SeatPosition.Left => _leftHandView!,
            _ => _bottomHandView!,
        };

        private RectTransform CreateRegion(
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private static void ConfigureRegionAsCenteredRow(RectTransform region)
        {
            HorizontalLayoutGroup hlg = region.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
        }

        /// <summary>
        /// Lays a region out as a column. Side hands pass
        /// <see cref="TextAnchor.UpperCenter"/> so the stack always begins at
        /// the region's top edge, just under its seat profile — a centred
        /// column would ride up into that profile on a short screen.
        /// </summary>
        private static void ConfigureRegionAsColumn(RectTransform region, TextAnchor alignment)
        {
            VerticalLayoutGroup vlg = region.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = alignment;
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(4, 4, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
        }

        private static void ConfigureRegionAsVerticalStack(RectTransform region)
        {
            VerticalLayoutGroup vlg = region.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.LowerCenter;
            vlg.spacing = 12f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
        }

        private ChainView CreateChainViewInside(RectTransform parent)
        {
            GameObject go = new("ChainView", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            ChainView cv = go.AddComponent<ChainView>();
            cv.LeftZone!.Tapped += OnEndTapped;
            cv.RightZone!.Tapped += OnEndTapped;
            cv.LeftZone!.Dropped += OnTileDroppedOnEnd;
            cv.RightZone!.Dropped += OnTileDroppedOnEnd;
            return cv;
        }

        private HandView CreateHandView(
            PlayerId player,
            RectTransform parent,
            HandOrientation handOrientation,
            TileOrientation tileOrientation,
            bool includesStatus,
            float shortDim = TileView.ShortDim,
            float longDim = TileView.LongDim,
            float? fanStep = null,
            bool showName = true)
        {
            GameObject go = new($"Hand_{player.Value}", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            HandView hv = go.AddComponent<HandView>();
            hv.Init(handOrientation, tileOrientation, shortDim, longDim, fanStep, showName);

            if (player == HumanPlayer)
            {
                hv.TileClicked += OnHumanTileClicked;
                hv.TileDragStarted += OnHumanTileDragStarted;
                hv.TileDragEnded += OnHumanTileDragEnded;
                hv.TileSelected += OnHumanTileSelected;
                hv.TileDeselected += OnHumanTileDeselected;
            }

            if (includesStatus)
            {
                _statusView = CreateStatusViewInside(parent);
                _statusView.PassClicked += OnPassClicked;
            }
            return hv;
        }

        private GameStatusView CreateStatusViewInside(RectTransform parent)
        {
            GameObject go = new("StatusView", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            return go.AddComponent<GameStatusView>();
        }

        // ---- Home button + return-to-lobby teardown ----------------------

        /// <summary>
        /// Small house-icon button anchored top-left of the board. Tapping
        /// it tears down the current round (offline: just drops the state;
        /// online: shuts down the NetworkRunner) and brings the lobby back
        /// so the player can start a new game without closing the app.
        /// </summary>
        private void BuildHomeButton()
        {
            GameObject go = new("HomeButton", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -16f);
            rt.sizeDelta = new Vector2(80f, 60f);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.40f, 0.24f, 0.85f);
            bg.raycastTarget = true;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(OnHomePressed);

            GameObject labelGo = new("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, worldPositionStays: false);
            RectTransform labelRt = (RectTransform)labelGo.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TMPro.TextMeshProUGUI tmp = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.fontSize = 24f;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            tmp.color = new Color(0.97f, 0.95f, 0.88f);
            tmp.text = "Home";
            tmp.raycastTarget = false;
        }

        // ---- End-of-round / opponent-left overlay ------------------------

        private void BuildEndOverlay()
        {
            GameObject go = new("EndOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _endOverlay = go.AddComponent<EndOverlayView>();
            _endOverlay.PrimaryClicked += OnOverlayPrimary;
            _endOverlay.SecondaryClicked += OnOverlaySecondary;
        }

        /// <summary>
        /// Reconciles the overlay with the current state on every Render. Shows
        /// the opponent-left prompt (highest priority), then the round-over
        /// prompt, and hides the overlay whenever a round is in progress.
        /// </summary>
        private void RefreshEndOverlay(MatchState state)
        {
            if (_endOverlay == null)
            {
                return;
            }

            if (_isOnline && _opponentLeft)
            {
                _overlayMode = OverlayMode.OpponentLeft;
                // Priority: (1) everyone left and we ended locally with a win;
                // (2) the leave ended the round (a departed seat was resigned) —
                // lead with the outcome; (3) otherwise just report the leave.
                // Rematch is never offered — a departed opponent can't play on.
                bool endedByLeave = state.IsOver;
                string leaveTitle = _abandonedWin
                    ? L10n.Get("end_you_win_opponent_left")
                    : endedByLeave
                        ? FormatStatus(state, isLocalTurn: false)
                        : L10n.Get("end_opponent_left");
                string? leaveSubtitle = (!_abandonedWin && endedByLeave)
                    ? L10n.Get("end_opponent_left")
                    : null;
                _endOverlay.Show(
                    title: leaveTitle,
                    subtitle: leaveSubtitle,
                    primaryLabel: null,
                    primaryInteractable: false,
                    secondaryLabel: L10n.Get("btn_back_to_lobby"));
                return;
            }

            // Match series (M5): the match-over screen, or between rounds a brief
            // interstitial that auto-advances (no rematch vote for series play).
            if (_isOnline && _onlineMatchController != null && _onlineMatchController.IsSeries)
            {
                if (_matchEnded || _onlineMatchController.MatchIsOver)
                {
                    _overlayMode = OverlayMode.MatchOver;
                    _endOverlay.Show(
                        title: SeriesWinnerText(),
                        subtitle: SeriesScoresText(),
                        primaryLabel: null,
                        primaryInteractable: false,
                        secondaryLabel: L10n.Get("btn_back_to_lobby"));
                    return;
                }
                if (!state.IsOver)
                {
                    _seriesInterstitialShown = false;
                    _endOverlay.Hide();
                    return;
                }
                // Round over, match continues → shows for ~10s (with a countdown)
                // then auto-advances. If the next round is a cut-throat battle, the
                // popup announces it. Shown once per round-over; the countdown
                // coroutine then owns the subtitle.
                _overlayMode = OverlayMode.RoundOver;
                if (!_seriesInterstitialShown)
                {
                    _seriesInterstitialShown = true;
                    bool battle = _onlineMatchController.PendingBattle;

                    // A KEY win "mashes up the board": scatter the laid tiles once,
                    // as the interstitial announces the bonus.
                    if (_rules.GetOutcome(state)?.IsKey == true)
                    {
                        _chainView?.MashUp();
                    }

                    _endOverlay.Show(
                        title: battle ? BattleTitleText() : SeriesRoundAwardText(state),
                        subtitle: string.Empty,
                        primaryLabel: null,
                        primaryInteractable: false,
                        secondaryLabel: null);
                    StartSeriesCountdown(battle);
                }
                return;
            }

            if (!state.IsOver)
            {
                _endOverlay.Hide();
                return;
            }

            _overlayMode = OverlayMode.RoundOver;
            string title = FormatStatus(state, isLocalTurn: false);
            string secondary = L10n.Get("btn_back_to_lobby");

            if (!_isOnline)
            {
                _endOverlay.Show(
                    title,
                    subtitle: null,
                    primaryLabel: L10n.Get("btn_play_again"),
                    primaryInteractable: true,
                    secondaryLabel: secondary);
                return;
            }

            // Online: rematch requires both players. Reflect our own vote as a
            // disabled "waiting…" button, and surface the opponent's vote as a
            // subtitle nudge.
            bool localVoted = _onlineMatchController!.LocalWantsRematch;
            bool opponentVoted = _onlineMatchController.OpponentWantsRematch;
            _endOverlay.Show(
                title,
                subtitle: opponentVoted ? L10n.Get("end_opponent_wants_rematch") : null,
                primaryLabel: localVoted
                    ? L10n.Get("btn_rematch_waiting")
                    : L10n.Get("btn_rematch"),
                primaryInteractable: !localVoted,
                secondaryLabel: secondary);
        }

        private string SeriesWinnerText()
        {
            OnlineMatchController? c = _onlineMatchController;
            if (c == null || _state == null)
            {
                return L10n.Get("series_match_over");
            }
            int seat = c.WinnerSeat;
            if (seat < 0 || seat >= _state.Players.Count)
            {
                return L10n.Get("series_match_over");
            }
            string name = c.IsBotSeat(seat) ? L10n.Get("player_bot") : _state.Players[seat].Value;
            return L10n.Get("series_winner", name);
        }

        private string SeriesScoresText()
        {
            OnlineMatchController? c = _onlineMatchController;
            if (c == null || _state == null)
            {
                return string.Empty;
            }
            string body = string.Empty;
            for (int i = 0; i < _state.Players.Count; i++)
            {
                string name = c.IsBotSeat(i) ? L10n.Get("player_bot") : _state.Players[i].Value;
                int points = c.SeriesPointsForSeat(i);
                // A player who ends on zero got "love".
                string tag = points == 0 ? "   " + L10n.Get("series_love") : string.Empty;
                body += (i == 0 ? string.Empty : "\n") + $"{name}   {points}{tag}";
            }
            return body;
        }

        // "⚔ BATTLE — Sly vs Noble" for the interstitial before a battle round.
        private string BattleTitleText()
        {
            OnlineMatchController? c = _onlineMatchController;
            if (c == null || _state == null)
            {
                return L10n.Get("battle_title");
            }
            List<string> names = new();
            for (int i = 0; i < _state.Players.Count; i++)
            {
                if (c.IsBattleSeat(i))
                {
                    names.Add(c.IsBotSeat(i) ? L10n.Get("player_bot") : _state.Players[i].Value);
                }
            }
            return L10n.Get("battle_title") + "\n" + string.Join(" vs ", names);
        }

        private void StartSeriesCountdown(bool battle)
        {
            if (_seriesCountdownRoutine != null)
            {
                StopCoroutine(_seriesCountdownRoutine);
            }
            _seriesCountdownRoutine = StartCoroutine(SeriesCountdownRoutine(battle));
        }

        private IEnumerator SeriesCountdownRoutine(bool battle)
        {
            int seconds = Mathf.RoundToInt(OnlineMatchController.SeriesAdvanceDelaySeconds);
            for (int n = seconds; n >= 1; n--)
            {
                if (_endOverlay != null)
                {
                    string line = L10n.Get(battle ? "battle_countdown" : "series_countdown", n);
                    _endOverlay.SetSubtitle(battle ? L10n.Get("battle_desc") + "\n" + line : line);
                }
                yield return new WaitForSeconds(1f);
            }
            _seriesCountdownRoutine = null;
        }

        // "<winner> wins the round  +1000" for the between-rounds interstitial.
        private string SeriesRoundAwardText(MatchState state)
        {
            MatchOutcome? outcome = _rules.GetOutcome(state);
            if (outcome?.WinnerId == null)
            {
                return L10n.Get("series_next_round");
            }
            string name = outcome.WinnerId.Value.Value;
            for (int i = 0; i < state.Players.Count; i++)
            {
                if (state.Players[i].Equals(outcome.WinnerId.Value) && _onlineMatchController != null
                    && _onlineMatchController.IsBotSeat(i))
                {
                    name = L10n.Get("player_bot");
                    break;
                }
            }

            // A key (both-ends lock-out) scores the bonus and gets its own banner
            // — "KEY! <winner> mash up the board  +2000".
            if (outcome.IsKey)
            {
                return L10n.Get("series_key_award", name, MatchFormatRules.KeyPoints);
            }

            return L10n.Get("series_round_award", name, MatchFormatRules.PointsPerRoundWin);
        }

        private void OnOverlayPrimary()
        {
            switch (_overlayMode)
            {
                case OverlayMode.RoundOver when _isOnline:
                    // Rematch. Overlay updates to "waiting…" via RematchVotesChanged;
                    // the re-deal arrives through OnOnlineRoundStarted.
                    _onlineMatchController?.TryRequestRematch();
                    break;

                case OverlayMode.RoundOver:
                    DealOfflineRound();
                    break;

                // OpponentLeft has no primary button.
            }
        }

        private void OnOverlaySecondary()
        {
            ReturnToLobby();
        }

        // ---- chat (ADR 0023) ------------------------------------------------
        //
        // The panel renders; ChatService talks; this wires the two together and
        // owns the room lifetime. Nothing here is a security boundary — the
        // server re-checks guest status, membership, mutes and rate limits on
        // every send — so the worst a tampered client achieves is a composer
        // that looks unlocked and a message that is still refused.

        private ChatPanelView? _chatPanel;
        private IDisposable? _chatSubscription;
        private string? _chatRoomId;
        private bool _chatJoinInFlight;
        private bool _chatMuted;

        private void CreateChatPanel()
        {
            GameObject go = new("ChatPanelView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _chatPanel = go.AddComponent<ChatPanelView>();
            _chatPanel.Init();
            _chatPanel.SendRequested += OnChatSendRequested;
            _chatPanel.ReportRequested += OnChatReportRequested;
            _chatPanel.CreateAccountRequested += OnChatCreateAccountRequested;
        }

        private async void OnChatPressed()
        {
            if (_chatPanel == null)
            {
                return;
            }
            if (_chatPanel.IsOpen)
            {
                _chatPanel.Close();
                return;
            }

            _chatPanel.SetLocalUid(AuthService.Instance?.Uid);
            _chatPanel.SetSubtitle(ChatSubtitle());
            RefreshChatEntitlement();
            _chatPanel.Open();

            // Join lazily, on first open: most matches never open chat, and a
            // room nobody looks at is a listener and a document for nothing.
            await EnsureChatRoomAsync();
        }

        /// <summary>
        /// Joins the chat room for the current table and starts listening. The
        /// room id is the Photon session name, so one room spans every round of a
        /// series; a practice match against bots has no session and so no chat.
        /// </summary>
        private async Task EnsureChatRoomAsync()
        {
            if (_chatPanel == null || _chatRoomId != null || _chatJoinInFlight)
            {
                return;
            }

            string? roomId = PhotonBootstrap.Instance?.CurrentRoomCode;
            if (!_isOnline || string.IsNullOrEmpty(roomId))
            {
                RefreshChatEntitlement();
                return;
            }

            _chatJoinInFlight = true;
            try
            {
                ChatService.JoinResult? joined = await ChatService.JoinRoomAsync(
                    roomId!,
                    ProfileService.Instance?.Profile?.DisplayName ?? L10n.Get("chat_you"),
                    _onlineMatchController?.LocalPlayerIndex ?? -1,
                    _onlineMatchController?.CurrentMatchId,
                    _onlineMatchController != null
                        ? _onlineMatchController.Mode.ToString().ToLowerInvariant()
                        : "unknown");

                if (joined == null)
                {
                    _chatPanel.SetStatus(L10n.Get("chat_error_join"), isError: true);
                    return;
                }

                _chatRoomId = joined.Value.RoomId;
                _chatSubscription = ChatService.Subscribe(_chatRoomId, OnChatMessages);
                _chatPanel.SetSubtitle(ChatSubtitle());
                RefreshChatEntitlement();
            }
            finally
            {
                _chatJoinInFlight = false;
            }
        }

        private void OnChatMessages(IReadOnlyList<ChatMessage> messages)
        {
            _chatPanel?.SetMessages(messages);
        }

        private void RefreshChatEntitlement()
        {
            AuthService? auth = AuthService.Instance;
            _chatPanel?.SetEntitlement(ChatEntitlement.For(
                isSignedIn: auth?.IsSignedIn ?? false,
                isGuest: auth?.IsGuest ?? true,
                isMuted: _chatMuted,
                hasRoom: _chatRoomId != null));
        }

        private async void OnChatSendRequested(string text)
        {
            if (_chatPanel == null || _chatRoomId == null)
            {
                return;
            }

            ChatService.SendResult result = await ChatService.SendAsync(_chatRoomId, text);
            switch (result.Outcome)
            {
                case ChatSendOutcome.Ok:
                    _chatPanel.ClearDraft();
                    // Say so, rather than leaving the player to wonder why their
                    // message came back with asterisks in it.
                    _chatPanel.SetStatus(result.Filtered ? L10n.Get("chat_filtered_notice") : string.Empty);
                    break;

                case ChatSendOutcome.GuestRestricted:
                    RefreshChatEntitlement();
                    break;

                case ChatSendOutcome.Muted:
                    // The server is the authority on mutes; reflect it locally so
                    // the composer stops offering what will only be refused.
                    _chatMuted = true;
                    RefreshChatEntitlement();
                    _chatPanel.SetStatus(L10n.Get("chat_locked_muted"), isError: true);
                    break;

                case ChatSendOutcome.RateLimited:
                    _chatPanel.SetStatus(L10n.Get("chat_rate_limited"), isError: true);
                    break;

                default:
                    _chatPanel.SetStatus(L10n.Get("chat_error_send"), isError: true);
                    break;
            }
        }

        private async void OnChatReportRequested(string messageId, ChatReportReason reason, string note)
        {
            if (_chatPanel == null || _chatRoomId == null)
            {
                return;
            }

            bool filed = await ChatService.ReportAsync(_chatRoomId, messageId, reason, note);
            _chatPanel.SetStatus(
                L10n.Get(filed ? "chat_report_sent" : "chat_error_report"),
                isError: !filed);
        }

        /// <summary>
        /// The guest CTA. Facebook is the only upgrade that completes without
        /// leaving the table, so it is what the in-match button offers; the
        /// lobby's Account section still carries the email route. On success the
        /// session stops being anonymous and chat unlocks in place — linking
        /// keeps the same uid, so nothing earned as a guest is lost (ADR 0019).
        /// </summary>
        private async void OnChatCreateAccountRequested()
        {
            if (_chatPanel == null || AuthService.Instance == null)
            {
                return;
            }

            _chatPanel.SetStatus(L10n.Get("chat_linking"));
            try
            {
                bool linked = await AuthService.Instance.ConnectFacebookAsync();
                if (!linked)
                {
                    _chatPanel.SetStatus(L10n.Get("chat_link_elsewhere"));
                    return;
                }

                // Re-join so the room's roster carries the upgraded profile.
                _chatSubscription?.Dispose();
                _chatSubscription = null;
                _chatRoomId = null;
                await EnsureChatRoomAsync();
                _chatPanel.SetStatus(L10n.Get("chat_unlocked"));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BoardBootstrap] chat account upgrade failed: {e.Message}");
                _chatPanel.SetStatus(L10n.Get("chat_link_elsewhere"), isError: true);
            }
        }

        private string ChatSubtitle()
        {
            string room = PhotonBootstrap.Instance?.CurrentRoomCode ?? string.Empty;
            return string.IsNullOrEmpty(room)
                ? L10n.Get("chat_subtitle_offline")
                : L10n.Get("chat_subtitle", room);
        }

        private void LeaveChatRoom()
        {
            _chatSubscription?.Dispose();
            _chatSubscription = null;
            _chatRoomId = null;
            _chatMuted = false;
            _chatPanel?.Close();
        }

        private void OnDestroy() => LeaveChatRoom();

        private void OnHomePressed()
        {
            Debug.Log("[BoardBootstrap] Home pressed — returning to lobby.");
            ReturnToLobby();
        }

        /// <summary>
        /// When the OS pauses + resumes the app (screen timeout, switching
        /// apps, etc.), the static 2-tap selection in <see cref="TileView"/>
        /// can be left in a stale "this tile is selected" state that the
        /// fresh-after-resume input doesn't toggle off correctly — the user
        /// sees their first tap re-highlight a tile but the second tap not
        /// fire as a confirm. Force a clean slate on each focus regain.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                TileView.ClearSelection();
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (!isPaused)
            {
                TileView.ClearSelection();
            }
        }

        /// <summary>
        /// Resets the bootstrap to a pre-game state and re-shows the lobby.
        /// Online sessions: shuts the NetworkRunner down (which the other
        /// client sees as OpponentLeft). Offline sessions: just clears local
        /// state. Either way the board is wiped and the lobby reappears so
        /// the player can pick another game without quitting the app.
        /// </summary>
        // Unsubscribes, shuts down Photon and destroys the online controller.
        // Shared by full ReturnToLobby and the pre-deal "cancel matchmaking" path.
        private void DetachOnlineController()
        {
            if (_onlineMatchController == null)
            {
                return;
            }
            _onlineMatchController.MatchDealt -= OnOnlineMatchDealt;
            _onlineMatchController.RoundStarted -= OnOnlineRoundStarted;
            _onlineMatchController.MoveApplied -= OnOnlineMoveApplied;
            _onlineMatchController.RematchVotesChanged -= OnRematchVotesChanged;
            _onlineMatchController.WaitingChanged -= OnWaitingChanged;
            _onlineMatchController.JoinFailed -= OnJoinFailed;
            _onlineMatchController.OpponentLeft -= OnOpponentLeft;
            _onlineMatchController.SeatsChanged -= OnSeatsChanged;
            _onlineMatchController.MatchAbandonedWin -= OnMatchAbandonedWin;
            _onlineMatchController.SeriesChanged -= OnSeriesChanged;
            _onlineMatchController.MatchEnded -= OnMatchEnded;
            _onlineMatchController.ShutdownAndReturnToLobby();
            Destroy(_onlineMatchController.gameObject);
            _onlineMatchController = null;
        }

        // The player backed out of the waiting room before the deal — drop the
        // matchmaking session but leave the lobby up (it resets itself).
        private void OnWaitingCancelled()
        {
            Debug.Log("[BoardBootstrap] Matchmaking cancelled by the player.");
            DetachOnlineController();
            _isOnline = false;
        }

        private void ReturnToLobby()
        {
            // Stop any in-flight timers / bot loops.
            if (_autoPoseRoutine != null)
            {
                StopCoroutine(_autoPoseRoutine);
                _autoPoseRoutine = null;
            }
            if (_autoPassRoutine != null)
            {
                StopCoroutine(_autoPassRoutine);
                _autoPassRoutine = null;
            }
            if (_botRoutine != null)
            {
                StopCoroutine(_botRoutine);
                _botRoutine = null;
            }

            // Leaving the table leaves its chat: a live Firestore listener on a
            // room nobody is watching keeps a stream open and bills reads.
            LeaveChatRoom();

            // Tear down the online session (if any). This also drops Photon
            // room membership so the other player gets OnPlayerLeft.
            DetachOnlineController();

            // Wipe the board: empty chain + empty hands.
            _state = null;
            _isOnline = false;
            _opponentLeft = false;
            _abandonedWin = false;
            _matchEnded = false;
            _seriesInterstitialShown = false;
            _localPlayer = HumanPlayer;
            _firstBotMove = true;
            _endOverlay?.Hide();
            TileView.ClearSelection();

            _handViewByPlayer.Clear();
            if (_bottomHandView != null) _bottomHandView.gameObject.SetActive(false);
            if (_rightHandView != null) _rightHandView.gameObject.SetActive(false);
            if (_topHandView != null) _topHandView.gameObject.SetActive(false);
            if (_leftHandView != null) _leftHandView.gameObject.SetActive(false);
            _chainView!.Setup(Chain.Empty);
            _statusView!.Setup(string.Empty, passEnabled: false, isOver: false);

            // Swap back to the splash sprite under the lobby (the lobby's own
            // poselobby art covers it, but if the lobby is later dismissed
            // for a NEW game we'll re-apply the board sprite in StartGame).
            ApplyRootSprite(_splashBackgroundSprite);

            ShowLobby();
        }
    }
}
