#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Pose.Core;
using Pose.Net;
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

        private const float TopBottomBandHeight = 180f;
        private const float StatusFooterHeight = 90f;
        private const float SideBandWidth = 150f;
        private const float RegionPadding = 16f;
        // Opponent's seat sits ~60 px below the top edge so it reads as in-game
        // rather than glued to the safe-area boundary.
        private const float TopRegionTopMargin = 60f;

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

        // Flag so we only fire the settlement Cloud Function once per round,
        // not on every subsequent Render call that observes the same finished
        // state.
        private bool _resultSubmitted;

        // Latched when the online opponent leaves the Photon session. The
        // opponent-left overlay outranks the round-over overlay: there is
        // nobody left to rematch with, so the only offer is back-to-lobby.
        private bool _opponentLeft;

        // Bumped per offline "Play again" so each practice round deals a
        // different hand. Derived from SpikeSeed rather than a system RNG so
        // any given round remains reproducible from (SpikeSeed, index).
        private int _offlineRoundIndex;

        private void Start()
        {
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
            Debug.Log($"[BoardBootstrap] Auth ready, uid: {FirebaseBootstrap.Instance!.Uid}");
            LoadProfile();
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
                ps.LoadOrCreate(FirebaseBootstrap.Instance!.Uid!);
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

        private static void EnsureStatsService()
        {
            if (StatsService.Instance != null)
            {
                return;
            }
            GameObject go = new("StatsService");
            go.AddComponent<StatsService>();
        }

        private void OnProfileReady()
        {
            UnsubscribeFromProfile();
            UserProfile profile = ProfileService.Instance!.Profile!;
            Debug.Log(
                $"[BoardBootstrap] Profile ready: \"{profile.DisplayName}\" " +
                $"({(ProfileService.Instance.IsNewProfile ? "new" : "returning")} player)");
            // Stats submission goes through a Cloud Function; ensure the
            // client-side StatsService exists before the round ends.
            EnsureStatsService();
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
        [Tooltip("Drag the lobby background sprite (poselobby.png in " +
                 "Assets/images) here. Falls back to the felt-green panel " +
                 "color if left empty.")]
        private Sprite? _lobbyBackgroundSprite;

        [SerializeField]
        [Tooltip("Drag the board background sprite (board.png in " +
                 "Assets/images) here. Falls back to the felt-green color " +
                 "if left empty.")]
        private Sprite? _boardBackgroundSprite;

        [SerializeField]
        [Tooltip("Drag the splash / loading sprite (posescreen.png in " +
                 "Assets/images) here. Shown during Firebase init before the " +
                 "lobby appears.")]
        private Sprite? _splashBackgroundSprite;

        private Image? _rootBackground;

        private LobbyView? _lobbyView;
        private OnlineMatchController? _onlineMatchController;
        private Coroutine? _autoPoseRoutine;
        private Coroutine? _autoPassRoutine;
        private Coroutine? _waitingTimerRoutine;

        // Seconds the host waits for the room to fill before offering to start
        // short with whoever is present.
        private const float WaitingTimeoutSeconds = 120f;

        // What the shared EndOverlayView is currently presenting, so its two
        // buttons dispatch to the right action.
        private enum OverlayMode { RoundOver, OpponentLeft, ShortStart }
        private OverlayMode _overlayMode = OverlayMode.RoundOver;

        private void ShowLobby()
        {
            if (_lobbyView != null)
            {
                return;
            }
            GameObject go = new("LobbyView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _lobbyView = go.AddComponent<LobbyView>();
            _lobbyView.SetBackgroundSprite(_lobbyBackgroundSprite);
            _lobbyView.PracticeChosen += OnPracticeChosen;
            _lobbyView.OnlineRoomActive += OnOnlineRoomActive;
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

        private void OnOnlineRoomActive(string roomCode, int playerCount)
        {
            Debug.Log(
                $"[BoardBootstrap] Online room active: {roomCode} (count={playerCount}) — " +
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

            string localPlayerId = ProfileService.Instance?.Profile?.DisplayName ?? "anon";

            GameObject go = new("OnlineMatchController");
            _onlineMatchController = go.AddComponent<OnlineMatchController>();
            _onlineMatchController.MatchDealt += OnOnlineMatchDealt;
            _onlineMatchController.RoundStarted += OnOnlineRoundStarted;
            _onlineMatchController.MoveApplied += OnOnlineMoveApplied;
            _onlineMatchController.RematchVotesChanged += OnRematchVotesChanged;
            _onlineMatchController.WaitingChanged += OnWaitingChanged;
            _onlineMatchController.OpponentLeft += OnOpponentLeft;
            _onlineMatchController.Setup(
                _networkedMatchPrefab,
                PhotonBootstrap.Instance.Runner,
                localPlayerId,
                playerCount);

            // Host of a 3+ player room: start a fill-timeout so we don't wait
            // forever for the last seat. The joiner path (playerCount 0) and 2P
            // rooms don't arm it — 2P can't start with fewer than 2.
            if (_onlineMatchController.IsHost && playerCount >= 3)
            {
                _waitingTimerRoutine = StartCoroutine(WaitingTimeoutRoutine());
            }

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

            // The room started (filled or short-started): stop the fill timer
            // and clear any short-start prompt.
            StopWaitingTimer();
            if (_overlayMode == OverlayMode.ShortStart)
            {
                _endOverlay?.Hide();
            }

            // Lobby served its purpose; hand off to the board.
            if (_lobbyView != null)
            {
                UnsubscribeFromLobby();
                Destroy(_lobbyView.gameObject);
                _lobbyView = null;
            }

            Render();
        }

        private void OnOnlineRoundStarted(MatchState state)
        {
            // A rematch was agreed and re-dealt. Seating and backgrounds are
            // already correct from the first deal; we just reset per-round
            // state and take the overlay down.
            _state = state;
            _localPlayer = _onlineMatchController!.LocalPlayer!.Value;
            _resultSubmitted = false;
            TileView.ClearSelection();
            _endOverlay?.Hide();
            Render();
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

        // ---- Pre-deal waiting + short-start (M3.7) ------------------------

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

        private IEnumerator WaitingTimeoutRoutine()
        {
            yield return new WaitForSeconds(WaitingTimeoutSeconds);
            _waitingTimerRoutine = null;

            // Only prompt if we're still waiting (not yet dealt) and have enough
            // players for a valid short game.
            if (_isOnline || _onlineMatchController == null)
            {
                yield break;
            }
            int have = _onlineMatchController.RegisteredCount;
            int want = _onlineMatchController.TargetPlayerCount;
            if (have >= 2 && have < want)
            {
                ShowShortStartPrompt(have);
            }
        }

        private void ShowShortStartPrompt(int currentCount)
        {
            _overlayMode = OverlayMode.ShortStart;
            _endOverlay?.Show(
                title: L10n.Get("waiting_short_start", currentCount),
                subtitle: null,
                primaryLabel: L10n.Get("btn_start_now"),
                primaryInteractable: true,
                secondaryLabel: L10n.Get("btn_keep_waiting"));
        }

        private void StopWaitingTimer()
        {
            if (_waitingTimerRoutine != null)
            {
                StopCoroutine(_waitingTimerRoutine);
                _waitingTimerRoutine = null;
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
            _resultSubmitted = false;
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
            ScheduleBotIfNeeded();
        }

        // ---- Click handler (unambiguous play) -----------------------------

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
                // Online: opponent's tiles render as backs (we know HOW MANY,
                // not WHICH). Offline (hot-seat): all hands visible.
                bool showBacks = _isOnline && !isLocal;
                hv.Setup(p.Value, isCurrent, state.Hands[p], predicate, showBacks);
            }

            _statusView!.Setup(
                FormatStatus(state, isLocalTurn),
                passEnabled: isLocalTurn && currentPlayerHasPass,
                isOver: state.IsOver);

            // Once per round, when state.IsOver becomes true, submit the
            // result to the settlement Cloud Function. The flag avoids
            // resubmitting on subsequent Renders that may fire for the same
            // finished state (e.g. drag-end animations).
            //
            // Online rounds skip this — M2.3's submit path is single-player
            // only; the online settlement validator (M4) takes its inputs
            // from the move log + seed, not the client.
            if (state.IsOver && !_resultSubmitted && !_isOnline)
            {
                _resultSubmitted = true;
                SubmitRoundResultIfPossible(state);
            }

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

        private void SubmitRoundResultIfPossible(MatchState state)
        {
            MatchOutcome? outcome = _rules.GetOutcome(state);
            if (outcome == null)
            {
                return;
            }
            if (StatsService.Instance == null)
            {
                Debug.LogWarning(
                    "[BoardBootstrap] StatsService missing — skipping result submit.");
                return;
            }
            StatsService.Instance.SubmitRoundResult(outcome, HumanPlayer);
        }

        private string FormatStatus(MatchState state, bool isLocalTurn)
        {
            if (state.IsOver)
            {
                MatchOutcome? outcome = _rules.GetOutcome(state);
                if (outcome != null)
                {
                    return FormatOutcome(outcome);
                }
            }

            return isLocalTurn
                ? L10n.Get("status_your_turn", state.CurrentPlayer.Value)
                : L10n.Get("status_waiting_for", state.CurrentPlayer.Value);
        }

        private static string FormatOutcome(MatchOutcome outcome)
        {
            string reasonKey = outcome.Reason switch
            {
                MatchEndReason.Domino => "end_reason_domino",
                MatchEndReason.Blocked => "end_reason_block",
                _ => "end_reason_domino",
            };
            string reason = L10n.Get(reasonKey);

            if (outcome.IsDraw)
            {
                return L10n.Get("status_round_over_draw", reason);
            }

            return L10n.Get(
                "status_round_over_winner",
                reason,
                outcome.WinnerId!.Value.Value,
                outcome.WinnerScore);
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
            RectTransform topRegion = CreateRegion(
                "TopRegion",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(SideBandWidth + RegionPadding, -TopBottomBandHeight - TopRegionTopMargin),
                offsetMax: new Vector2(-(SideBandWidth + RegionPadding), -TopRegionTopMargin));
            ConfigureRegionAsCenteredRow(topRegion);

            RectTransform bottomRegion = CreateRegion(
                "BottomRegion",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0f),
                offsetMin: new Vector2(SideBandWidth + RegionPadding, 0f),
                offsetMax: new Vector2(-(SideBandWidth + RegionPadding), TopBottomBandHeight + StatusFooterHeight));
            ConfigureRegionAsVerticalStack(bottomRegion);

            RectTransform leftRegion = CreateRegion(
                "LeftRegion",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 1f),
                offsetMin: new Vector2(0f, TopBottomBandHeight + StatusFooterHeight + RegionPadding),
                offsetMax: new Vector2(SideBandWidth, -(TopBottomBandHeight + RegionPadding)));
            ConfigureRegionAsCenteredColumn(leftRegion);

            RectTransform rightRegion = CreateRegion(
                "RightRegion",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-SideBandWidth, TopBottomBandHeight + StatusFooterHeight + RegionPadding),
                offsetMax: new Vector2(0f, -(TopBottomBandHeight + RegionPadding)));
            ConfigureRegionAsCenteredColumn(rightRegion);

            RectTransform centerRegion = CreateRegion(
                "CenterRegion",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(SideBandWidth + RegionPadding, TopBottomBandHeight + StatusFooterHeight + RegionPadding),
                offsetMax: new Vector2(-(SideBandWidth + RegionPadding), -(TopBottomBandHeight + RegionPadding)));
            ConfigureRegionAsCenteredRow(centerRegion);

            _chainView = CreateChainViewInside(centerRegion);

            // Seats are built once at scene load. Player-to-seat binding (and
            // whether a seat is even used) is decided per-round when the deal
            // lands — see SeatPlayersForOffline / SeatPlayersForOnline.
            _bottomHandView = CreateHandView(
                Players[0], bottomRegion, HandOrientation.Horizontal, TileOrientation.Portrait, includesStatus: true);
            _rightHandView = CreateHandView(
                Players[1], rightRegion, HandOrientation.Vertical, TileOrientation.Landscape, includesStatus: false);
            _topHandView = CreateHandView(
                Players[2], topRegion, HandOrientation.Horizontal, TileOrientation.Portrait, includesStatus: false);
            _leftHandView = CreateHandView(
                Players[3], leftRegion, HandOrientation.Vertical, TileOrientation.Landscape, includesStatus: false);
        }

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

            for (int i = 0; i < players.Count; i++)
            {
                HandView seat = HandViewForSeat(seats[i]);
                _handViewByPlayer[players[i]] = seat;
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

        private static void ConfigureRegionAsCenteredColumn(RectTransform region)
        {
            VerticalLayoutGroup vlg = region.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleCenter;
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
            cv.LeftZone!.Dropped += OnTileDroppedOnEnd;
            cv.RightZone!.Dropped += OnTileDroppedOnEnd;
            return cv;
        }

        private HandView CreateHandView(
            PlayerId player,
            RectTransform parent,
            HandOrientation handOrientation,
            TileOrientation tileOrientation,
            bool includesStatus)
        {
            GameObject go = new($"Hand_{player.Value}", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            HandView hv = go.AddComponent<HandView>();
            hv.Init(handOrientation, tileOrientation);

            if (player == HumanPlayer)
            {
                hv.TileClicked += OnHumanTileClicked;
                hv.TileDragStarted += OnHumanTileDragStarted;
                hv.TileDragEnded += OnHumanTileDragEnded;
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
                _endOverlay.Show(
                    title: L10n.Get("end_opponent_left"),
                    subtitle: null,
                    primaryLabel: null,
                    primaryInteractable: false,
                    secondaryLabel: L10n.Get("btn_back_to_lobby"));
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

        private void OnOverlayPrimary()
        {
            switch (_overlayMode)
            {
                case OverlayMode.ShortStart:
                    // Host accepted a short game — deal with who's present. The
                    // deal lands via OnOnlineMatchDealt, which hides this prompt.
                    _onlineMatchController?.StartWithCurrentPlayers();
                    break;

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
            if (_overlayMode == OverlayMode.ShortStart)
            {
                // Keep waiting — dismiss and re-arm the fill timer.
                _endOverlay?.Hide();
                StopWaitingTimer();
                _waitingTimerRoutine = StartCoroutine(WaitingTimeoutRoutine());
                return;
            }

            ReturnToLobby();
        }

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
            StopWaitingTimer();

            // Tear down the online session (if any). This also drops Photon
            // room membership so the other player gets OnPlayerLeft.
            if (_onlineMatchController != null)
            {
                _onlineMatchController.MatchDealt -= OnOnlineMatchDealt;
                _onlineMatchController.RoundStarted -= OnOnlineRoundStarted;
                _onlineMatchController.MoveApplied -= OnOnlineMoveApplied;
                _onlineMatchController.RematchVotesChanged -= OnRematchVotesChanged;
                _onlineMatchController.WaitingChanged -= OnWaitingChanged;
                _onlineMatchController.OpponentLeft -= OnOpponentLeft;
                _onlineMatchController.ShutdownAndReturnToLobby();
                Destroy(_onlineMatchController.gameObject);
                _onlineMatchController = null;
            }

            // Wipe the board: empty chain + empty hands.
            _state = null;
            _isOnline = false;
            _opponentLeft = false;
            _localPlayer = HumanPlayer;
            _resultSubmitted = false;
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
