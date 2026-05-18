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
        private GameStatusView? _statusView;
        private Coroutine? _botRoutine;
        private bool _firstBotMove = true;
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

        // ---- Lobby (M3.2) --------------------------------------------------

        private LobbyView? _lobbyView;

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
            // M3.2 stops here — the player is connected to a Photon room, but
            // networked gameplay (deal + move sync) lands in M3.3. For now the
            // lobby view stays up with its "waiting/connected" message; the
            // status footer underneath isn't touched because the board hasn't
            // been started.
            Debug.Log($"[BoardBootstrap] Online room active: {roomCode} (M3.2 spike ends here)");
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
            if (_state.CurrentPlayer != HumanPlayer)
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
                    ApplyMove(pm);
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
            if (_state.CurrentPlayer != HumanPlayer)
            {
                return;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PlaceMove pm && pm.Tile == tv.Tile && pm.End == end)
                {
                    tv.NotifyDropAccepted();
                    ApplyMove(pm);
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
            if (_state.CurrentPlayer != HumanPlayer)
            {
                return;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PassMove pass)
                {
                    ApplyMove(pass);
                    return;
                }
            }
        }

        private void ApplyMove(Move move)
        {
            _state = _rules.Apply(_state!, move);
            Render();
            ScheduleBotIfNeeded();
        }

        // ---- Bot loop -----------------------------------------------------

        private void ScheduleBotIfNeeded()
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }
            if (_state.CurrentPlayer == HumanPlayer)
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
            while (_state != null && !_state.IsOver && _state.CurrentPlayer != HumanPlayer)
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

            // Per-tile interaction mode for the human's hand.
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

            bool isHumansTurn = !state.IsOver && state.CurrentPlayer == HumanPlayer;

            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                bool isCurrent = !state.IsOver && p == state.CurrentPlayer;
                Func<Tile, TileInteractionMode>? predicate =
                    (isHumansTurn && p == HumanPlayer)
                        ? new Func<Tile, TileInteractionMode>(tile =>
                            tileModes.TryGetValue(tile, out TileInteractionMode m)
                                ? m
                                : TileInteractionMode.None)
                        : null;
                _handViewByPlayer[p].Setup(p.Value, isCurrent, state.Hands[p], predicate);
            }

            _statusView!.Setup(
                FormatStatus(state, isHumansTurn),
                passEnabled: isHumansTurn && currentPlayerHasPass,
                isOver: state.IsOver);

            // Once per round, when state.IsOver becomes true, submit the
            // result to the settlement Cloud Function. The flag avoids
            // resubmitting on subsequent Renders that may fire for the same
            // finished state (e.g. drag-end animations).
            if (state.IsOver && !_resultSubmitted)
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

        private string FormatStatus(MatchState state, bool isHumansTurn)
        {
            if (state.IsOver)
            {
                MatchOutcome? outcome = _rules.GetOutcome(state);
                if (outcome != null)
                {
                    return FormatOutcome(outcome);
                }
            }

            return isHumansTurn
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

            _handViewByPlayer[Players[0]] = CreateHandView(
                Players[0], bottomRegion, HandOrientation.Horizontal, TileOrientation.Portrait, includesStatus: true);
            _handViewByPlayer[Players[1]] = CreateHandView(
                Players[1], rightRegion, HandOrientation.Vertical, TileOrientation.Landscape, includesStatus: false);
            _handViewByPlayer[Players[2]] = CreateHandView(
                Players[2], topRegion, HandOrientation.Horizontal, TileOrientation.Portrait, includesStatus: false);
            _handViewByPlayer[Players[3]] = CreateHandView(
                Players[3], leftRegion, HandOrientation.Vertical, TileOrientation.Landscape, includesStatus: false);
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
