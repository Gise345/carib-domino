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

        private static readonly Color FeltColor = new(0.05f, 0.30f, 0.18f, 1f);

        private const float TopBottomBandHeight = 180f;
        private const float StatusFooterHeight = 90f;
        private const float SideBandWidth = 150f;
        private const float RegionPadding = 16f;

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

        private void Start()
        {
            ConfigureRoot();
            BuildSpatialLayout();

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

        private LobbyView? _lobbyView;
        private OnlineMatchController? _onlineMatchController;

        private void ShowLobby()
        {
            if (_lobbyView != null)
            {
                return;
            }
            GameObject go = new("LobbyView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            _lobbyView = go.AddComponent<LobbyView>();
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

        private void OnOnlineRoomActive(string roomCode)
        {
            Debug.Log($"[BoardBootstrap] Online room active: {roomCode} — starting OnlineMatchController");

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
            _onlineMatchController.MoveApplied += OnOnlineMoveApplied;
            _onlineMatchController.Setup(
                _networkedMatchPrefab,
                PhotonBootstrap.Instance.Runner,
                localPlayerId);
            // The lobby stays on screen showing "Waiting for opponent /
            // Connected to room <code>" until the deal lands — at which point
            // OnOnlineMatchDealt destroys it and the board takes over.
        }

        // ---- Online deal + move hooks (M3.4) ------------------------------

        private void OnOnlineMatchDealt(MatchState state)
        {
            _isOnline = true;
            _state = state;
            _localPlayer = _onlineMatchController!.LocalPlayer!.Value;
            SeatPlayersForOnline(state.Players, _localPlayer);

            // Lobby served its purpose; hand off to the board.
            if (_lobbyView != null)
            {
                UnsubscribeFromLobby();
                Destroy(_lobbyView.gameObject);
                _lobbyView = null;
            }

            Render();
        }

        private void OnOnlineMoveApplied(MatchState state, Move move)
        {
            _state = state;
            Render();
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
            SeatPlayersForOffline();

            _state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                Players,
                Partnership.CutThroat(Players),
                new SeededRandomSource(SpikeSeed));

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

        // ---- Render -------------------------------------------------------

        private void Render()
        {
            MatchState state = _state!;
            _chainView!.Setup(state.Chain);

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

            Image background = gameObject.AddComponent<Image>();
            background.color = FeltColor;
        }

        private void BuildSpatialLayout()
        {
            RectTransform topRegion = CreateRegion(
                "TopRegion",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(SideBandWidth + RegionPadding, -TopBottomBandHeight),
                offsetMax: new Vector2(-(SideBandWidth + RegionPadding), 0f));
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
        /// Online 2P: local player at the bottom, opponent at the top, side
        /// seats hidden. This is symmetric — each phone sees themselves below
        /// regardless of who hosted.
        /// </summary>
        private void SeatPlayersForOnline(IReadOnlyList<PlayerId> players, PlayerId local)
        {
            _handViewByPlayer.Clear();
            PlayerId remote = players[0].Equals(local) ? players[1] : players[0];
            _handViewByPlayer[local] = _bottomHandView!;
            _handViewByPlayer[remote] = _topHandView!;
            _bottomHandView!.gameObject.SetActive(true);
            _topHandView!.gameObject.SetActive(true);
            _rightHandView!.gameObject.SetActive(false);
            _leftHandView!.gameObject.SetActive(false);
        }

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
    }
}
