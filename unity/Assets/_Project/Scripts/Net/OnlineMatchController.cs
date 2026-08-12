#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Pose.Core;
using UnityEngine;
// Both Fusion and Pose.Core declare a GameMode; this file only means the
// gameplay ruleset, so bind the bare name to ours (Fusion's is used only in
// PhotonBootstrap).
using GameMode = Pose.Core.GameMode;

namespace Pose.Net
{
    /// <summary>
    /// Orchestrates the 2-, 3- or 4-player online Cut-Throat round. Created by
    /// <see cref="Pose.Game.BoardBootstrap"/> once the lobby reports an
    /// online room is active. On <see cref="Setup"/>:
    /// <list type="bullet">
    ///   <item>If we're the shared-mode master client → we're host. Spawn
    ///         <see cref="NetworkedMatch"/> with a fresh seed and the target
    ///         player count, seating ourselves at index 0.</item>
    ///   <item>If a NetworkedMatch already exists (host spawned it before we
    ///         joined) → we're a joiner. Claim a seat via
    ///         <see cref="NetworkedMatch.RPC_RegisterPlayer"/>.</item>
    /// </list>
    /// Both sides subscribe to <see cref="NetworkedMatch.DealReadyChanged"/>;
    /// when every seat is filled it fires, each client runs <c>Dealer.Deal</c>
    /// locally against the synced inputs and gets the same
    /// <see cref="MatchState"/>.
    ///
    /// M3.4 adds move sync. After the deal, the host installs a move
    /// validator on the NetworkedMatch so its <see cref="NetworkedMatch.RPC_SubmitMove"/>
    /// can reject illegal submissions against the authoritative state. Both
    /// clients subscribe to <see cref="NetworkedMatch.MoveAppliedChanged"/>
    /// and apply newly-appended moves to their local
    /// <see cref="CurrentState"/>. The UI (BoardBootstrap) listens to
    /// <see cref="MoveApplied"/> for re-renders and calls
    /// <see cref="TrySubmitLocalMove"/> on user input.
    /// </summary>
    public sealed class OnlineMatchController : MonoBehaviour
    {
        // Set per deal from the match's GameMode — Cut-Throat or Jamaican Partner.
        private IRuleEngine _rules = new CutThroatRules();

        /// <summary>Fires once when the initial deal completes on this client.</summary>
        public event Action<MatchState>? MatchDealt;

        /// <summary>
        /// Fires when an agreed rematch has been dealt on this client, carrying
        /// the fresh <see cref="MatchState"/>. Distinct from
        /// <see cref="MatchDealt"/> so the UI can tear down round-over
        /// presentation rather than redo first-deal setup (seating, lobby
        /// dismissal) that is already correct.
        /// </summary>
        public event Action<MatchState>? RoundStarted;

        /// <summary>
        /// Fires whenever <see cref="LocalWantsRematch"/> or
        /// <see cref="OpponentWantsRematch"/> changes, so the round-over UI can
        /// reflect who has opted in.
        /// </summary>
        public event Action? RematchVotesChanged;

        /// <summary>
        /// Fires while waiting in the pre-deal lobby whenever the number of
        /// seated players changes, so the UI can show "3 of 4 joined…".
        /// </summary>
        public event Action? WaitingChanged;

        /// <summary>
        /// Fires after each replicated move has been applied to
        /// <see cref="CurrentState"/>. Payload is (newState, appliedMove).
        /// </summary>
        public event Action<MatchState, Move>? MoveApplied;

        /// <summary>
        /// Fires once on the remaining client when the runner detects the
        /// opponent has left the Photon session (either explicit back-to-lobby
        /// or app crash). Detected by polling <c>Runner.ActivePlayers.Count()</c>
        /// in <see cref="Update"/> — when the count drops, we fire. The UI uses
        /// this to offer "continue against bot" or "back to lobby".
        /// </summary>
        public event Action? OpponentLeft;

        /// <summary>
        /// Fires when a seat's membership changes without a move — a player left
        /// and was replaced by a bot (M3.9b) — so the board can re-render name
        /// plates ("Bot") immediately.
        /// </summary>
        public event Action? SeatsChanged;

        /// <summary>
        /// Fires on the last remaining human when everyone else has left a round
        /// that can no longer be settled authoritatively (e.g. the table's
        /// authority was the one who left). Carries a local "you win" outcome so
        /// the UI can end the round; casual only — no server stats (ADR 0011).
        /// </summary>
        public event Action? MatchAbandonedWin;

        /// <summary>
        /// The live local <see cref="MatchState"/> — advances as the networked
        /// move log replays. Null until the initial deal completes.
        /// </summary>
        public MatchState? CurrentState { get; private set; }

        /// <summary>This client's PlayerId in the dealt round.</summary>
        public PlayerId? LocalPlayer { get; private set; }

        /// <summary>
        /// This client's seat index into <c>CurrentState.Players</c>. The host is
        /// seat 0; joiners take 1, 2, 3 in the order they registered. Derived
        /// from the seat's owning <see cref="PlayerRef"/>, not join luck.
        /// </summary>
        public int LocalPlayerIndex { get; private set; } = -1;

        /// <summary>True once this client has opted into a rematch of the finished round.</summary>
        public bool LocalWantsRematch => ReadRematchVote(LocalPlayerIndex);

        /// <summary>
        /// True once ANY other seated player has opted into a rematch — the
        /// round-over UI surfaces this as a nudge; the re-deal still needs all
        /// seats to vote.
        /// </summary>
        public bool OpponentWantsRematch
        {
            get
            {
                if (_match == null || LocalPlayerIndex < 0)
                {
                    return false;
                }
                int others = _match.RematchVoteMask & ~(1 << LocalPlayerIndex) & ((1 << _match.PlayerCount) - 1);
                return others != 0;
            }
        }

        /// <summary>
        /// True once a player has left the Photon session. With more than two
        /// players a leave still ends the match (continuing minus a hand is
        /// deferred) — the UI offers only back-to-lobby.
        /// </summary>
        public bool OpponentHasLeft => _opponentLeftFired;

        /// <summary>How many players have taken a seat so far (pre-deal waiting).</summary>
        public int RegisteredCount => _match?.RegisteredCount ?? 0;

        /// <summary>The target player count for this room (host's pick).</summary>
        public int TargetPlayerCount => _match?.PlayerCount ?? 0;

        /// <summary>True if this client is the room host (shared-mode master client).</summary>
        public bool IsHost => _runner != null && _runner.IsSharedModeMasterClient;

        /// <summary>True if the seat at <paramref name="seat"/> is a bot (M3.9b).</summary>
        public bool IsBotSeat(int seat) => _match != null && _match.IsBotSeat(seat);

        // ---- Match series (M5) --------------------------------------------

        /// <summary>Fires when the series scores advance, so the scoreboard can refresh.</summary>
        public event Action? SeriesChanged;

        /// <summary>Fires when the match series ends, so the UI can show the winner.</summary>
        public event Action? MatchEnded;

        /// <summary>True for a Cut-Throat game, which is played as a scored series (M5).</summary>
        public bool IsSeries => _match != null && _match.GameMode == GameMode.CutThroat;

        /// <summary>The series format (Classic / Quick).</summary>
        public MatchFormat SeriesFormat => _match != null ? _match.SeriesFormat : MatchFormat.ClassicSixLove;

        /// <summary>The current round number in the series (1-based).</summary>
        public int SeriesRoundNumber => _match?.RoundNumber ?? 0;

        /// <summary>True once the series has been decided.</summary>
        public bool MatchIsOver => _match?.MatchOver ?? false;

        /// <summary>The winning seat once <see cref="MatchIsOver"/>, or -1.</summary>
        public int WinnerSeat => _match?.WinnerSeat ?? -1;

        /// <summary>A seat's cumulative series points.</summary>
        public int SeriesPointsForSeat(int seat) =>
            _match != null && seat >= 0 && seat < NetworkedMatch.MaxPlayers ? _match.SeriesPoints.Get(seat) : 0;

        private NetworkObject? _matchPrefab;
        private NetworkRunner? _runner;
        private string _localPlayerId = string.Empty;
        private string _localUid = string.Empty;
        private int _targetPlayerCount = 2;
        private GameMode _targetMode = GameMode.CutThroat;
        private NetworkedMatch? _match;

        private int _lastSeenPlayerCount;
        private bool _opponentLeftFired;
        private bool _advancingRematch;
        private bool _settlementSubmitted;
        private bool _wasAuthority;
        private bool _localEndFired;

        // Match series (M5, Cut-Throat). _series is authority-only; every client
        // reads the replicated scores off NetworkedMatch for the scoreboard.
        private SeriesState? _series;
        private MatchFormat _targetFormat = MatchFormat.ClassicSixLove;
        private bool _seriesRoundProcessed;
        private Coroutine? _advanceRoutine;

        // Beat between a round ending and the next deal, so players read the result.
        private const float SeriesAdvanceDelaySeconds = 2.5f;

        // Seconds a table waits for humans before the authority fills the empty
        // seats with bots and deals. No one taps "start" — there is no host role.
        private const float AutoStartSeconds = 60f;

        // Drives bot seats on the table's authority. The chosen moves are recorded
        // in the (server-replayed) move log, so bot RNG needn't be deterministic
        // for settlement; it is seeded from the deal seed only for tidiness.
        private readonly RandomBot _bot = new();
        private IRandomSource _botRng = new SeededRandomSource(0UL);
        private Coroutine? _botRoutine;

        // Beat before a bot plays, so its moves read as deliberate rather than
        // instant. Mirrors the offline bot cadence.
        private const float BotMoveDelaySeconds = 1.2f;

        /// <summary>
        /// The server-issued match id for the current round, or empty if the seed
        /// was a local fallback. M4.3's settlement submits the round log under this.
        /// </summary>
        public string CurrentMatchId => _match?.MatchId.ToString() ?? string.Empty;

        /// <param name="targetPlayerCount">
        /// The room size chosen at Create time (2–4). Used only when this client
        /// is the host; joiners read the count from the replicated match.
        /// </param>
        /// <param name="localUid">
        /// This client's Firebase uid, reported to the host at registration so
        /// settlement (M4.3) can attribute this seat's result. Empty is tolerated
        /// (that seat simply won't have stats written).
        /// </param>
        public void Setup(
            NetworkObject matchPrefab,
            NetworkRunner runner,
            string localPlayerId,
            string localUid,
            int targetPlayerCount,
            GameMode mode,
            MatchFormat format)
        {
            _matchPrefab = matchPrefab;
            _runner = runner;
            _localPlayerId = string.IsNullOrEmpty(localPlayerId) ? "anon" : localPlayerId;
            _localUid = localUid;
            _targetMode = mode;
            _targetFormat = format;
            // Jamaican Partner is always exactly 4 players.
            _targetPlayerCount = mode == GameMode.Partner
                ? NetworkedMatch.MaxPlayers
                : Mathf.Clamp(targetPlayerCount, 2, NetworkedMatch.MaxPlayers);

            NetworkedMatch.AnySpawned += OnNetworkedMatchSpawned;

            // Catch the host-already-spawned case (e.g. we joined an existing
            // room where the host's NetworkedMatch landed before our subscribe).
            // FindObjectOfType is OK at one-shot init time.
            NetworkedMatch existing = UnityEngine.Object.FindAnyObjectByType<NetworkedMatch>();
            if (existing != null)
            {
                OnNetworkedMatchSpawned(existing);
                return;
            }

            // Only the Photon shared-mode master client (the player who
            // created / first connected to the room) spawns the
            // NetworkedMatch. Other clients wait for AnySpawned to fire
            // when Fusion replicates the master's object to them.
            //
            // We used to gate on ActivePlayers.Count() <= 1, but that was
            // racy: a joiner could briefly see only themselves before the
            // host's player info propagated and would then incorrectly
            // spawn its own NetworkedMatch — both clients ended up
            // "hosting" their own copy and the deal handshake never
            // completed.
            if (_runner.IsSharedModeMasterClient)
            {
                _ = SpawnAsHostAsync();
            }
        }

        private void OnDestroy()
        {
            NetworkedMatch.AnySpawned -= OnNetworkedMatchSpawned;
            if (_match != null)
            {
                _match.DealReadyChanged -= OnDealReady;
                _match.RoundStartedChanged -= OnRoundStarted;
                _match.RematchVotesChanged -= OnRematchVotesChanged;
                _match.RegisteredCountChanged -= OnRegisteredCountChanged;
                _match.MoveAppliedChanged -= OnMoveAppliedChanged;
                _match.SeriesChanged -= OnSeriesChanged;
                _match.MatchOverChanged -= OnMatchOverChanged;
                if (_match.Object != null && _match.Object.HasStateAuthority)
                {
                    _match.MoveValidator = null;
                }
            }
        }

        private void Update()
        {
            if (_runner == null)
            {
                return;
            }

            // Authority can migrate at any time (the previous authority left).
            // When we become it, take over the host-side duties.
            bool authority = HasMatchAuthority;
            if (authority && !_wasAuthority)
            {
                OnBecameAuthority();
            }
            _wasAuthority = authority;

            int count = _runner.ActivePlayers.Count();
            if (_lastSeenPlayerCount == 0)
            {
                _lastSeenPlayerCount = count;
                return;
            }
            if (count < _lastSeenPlayerCount)
            {
                Debug.Log(
                    $"[OnlineMatchController] Player count dropped {_lastSeenPlayerCount} -> {count}.");
                HandleDepartures();
            }
            _lastSeenPlayerCount = count;

            // Safety net: if I'm the only one left in a round that can't settle
            // (the authority was the leaver and nothing migrated), end locally.
            MaybeLocalWinIfStranded();
        }

        private bool HasMatchAuthority =>
            _match != null && _match.Object != null && _match.Object.HasStateAuthority;

        /// <summary>
        /// Just became the table's authority (Fusion migrated it here after the
        /// previous authority left). Re-install the move validator and resume the
        /// host-side duties — auto-start deadline and bot-driving are already
        /// authority-gated, so they pick up automatically.
        /// </summary>
        private void OnBecameAuthority()
        {
            if (_match == null)
            {
                return;
            }
            Debug.Log("[OnlineMatchController] Became table authority (migration).");
            if (CurrentState != null)
            {
                _match.MoveValidator = ValidateNetworkedMove;
            }
            // A departed seat / bot's turn may be pending with no move to trigger
            // scheduling, and unresolved departures need handling under our new
            // authority.
            HandleDepartures();
            ScheduleBotIfNeeded();
        }

        /// <summary>
        /// Reacts to one or more players having left. On the authority: seats that
        /// vacated mid-round become bots and the table plays on while ≥2 humans
        /// remain (<see cref="SeatFillPolicy"/>); if only one human is left, the
        /// round ends by resigning a departed seat (the lone human wins + settles).
        /// Bot seats and already-resolved seats are skipped, so repeated leaves are
        /// handled idempotently.
        /// </summary>
        private void HandleDepartures()
        {
            if (_match == null || _runner == null || CurrentState == null || CurrentState.IsOver)
            {
                return;
            }
            if (!HasMatchAuthority)
            {
                return; // a non-authority can't act; migration or the local-win net covers it
            }

            HashSet<int> active = new();
            foreach (PlayerRef p in _runner.ActivePlayers)
            {
                active.Add(p.PlayerId);
            }

            int count = _match.PlayerCount;
            List<int> departed = new();
            int humansRemaining = 0;
            for (int seat = 0; seat < count; seat++)
            {
                if (_match.IsBotSeat(seat))
                {
                    continue;
                }
                if (active.Contains(_match.SeatPlayerRefs.Get(seat)))
                {
                    humansRemaining++;
                }
                else
                {
                    departed.Add(seat);
                }
            }

            if (departed.Count == 0)
            {
                return;
            }

            // Partner is team-aware: if a whole team has no active humans left,
            // the OTHER team wins the round outright (a resign on the abandoned
            // team). A single leaver whose partner is still present is bot-filled
            // so the 2-v-2 plays on.
            if (_match.GameMode == GameMode.Partner)
            {
                int abandonedSeat = FindAbandonedTeamSeat(active);
                if (abandonedSeat >= 0)
                {
                    Debug.Log($"[OnlineMatchController] A partner team abandoned — resigning seat {abandonedSeat}; the other team wins.");
                    _match.RPC_SubmitMove(NetworkedMove.FromResign((byte)abandonedSeat));
                    if (!_opponentLeftFired)
                    {
                        _opponentLeftFired = true;
                        OpponentLeft?.Invoke();
                    }
                    return;
                }

                foreach (int seat in departed)
                {
                    _match.MakeSeatBot(seat);
                }
                SeatsChanged?.Invoke();
                ScheduleBotIfNeeded();
                return;
            }

            if (SeatFillPolicy.OnLeave(humansRemaining) == SeatFillPolicy.LeaveAction.FillWithBots)
            {
                foreach (int seat in departed)
                {
                    _match.MakeSeatBot(seat);
                }
                SeatsChanged?.Invoke();  // re-render the "Bot" name plates now
                ScheduleBotIfNeeded();   // it may already be a bot seat's turn
            }
            else
            {
                // One human left — end the round by resigning a departed seat. The
                // engine awards it to the remaining player; settlement follows.
                Debug.Log($"[OnlineMatchController] Last human standing — resigning seat {departed[0]} to end the round.");
                _match.RPC_SubmitMove(NetworkedMove.FromResign((byte)departed[0]));
                if (!_opponentLeftFired)
                {
                    _opponentLeftFired = true;
                    OpponentLeft?.Invoke();
                }
            }
        }

        /// <summary>
        /// Partner-only. Returns a seat index on a team that has NO active humans
        /// left (both partners gone), so the caller can resign it and hand the
        /// round to the other team; -1 if every team still has a human. Bots don't
        /// count as humans — a team of two bots is abandoned.
        /// </summary>
        private int FindAbandonedTeamSeat(HashSet<int> active)
        {
            MatchState state = CurrentState!;
            Partnership partnership = state.Partnership;
            foreach (Team team in partnership.Teams)
            {
                bool anyHuman = false;
                int anySeat = -1;
                for (int seat = 0; seat < state.Players.Count; seat++)
                {
                    if (partnership.GetTeamOf(state.Players[seat]) != team.Id)
                    {
                        continue;
                    }
                    anySeat = seat;
                    if (!_match!.IsBotSeat(seat) && active.Contains(_match.SeatPlayerRefs.Get(seat)))
                    {
                        anyHuman = true;
                    }
                }
                if (!anyHuman && anySeat >= 0)
                {
                    return anySeat;
                }
            }
            return -1;
        }

        /// <summary>
        /// Last-human safety net: if everyone else has left a live round and this
        /// client can't settle it (the authority left and nothing migrated here),
        /// end locally with a win. Casual only — no server stats (ADR 0011).
        /// </summary>
        private void MaybeLocalWinIfStranded()
        {
            if (_localEndFired || _match == null || CurrentState == null)
            {
                return;
            }
            if (!_match.DealReady || CurrentState.IsOver)
            {
                return;
            }
            // Only fires when I'm truly alone AND I'm not the authority (if I were,
            // HandleDepartures would have ended the round properly).
            if (_runner!.ActivePlayers.Count() > 1 || HasMatchAuthority)
            {
                return;
            }
            _localEndFired = true;
            _opponentLeftFired = true;
            Debug.Log("[OnlineMatchController] Stranded as last human — ending locally with a win (casual).");
            MatchAbandonedWin?.Invoke();
        }

        // ---- Bot driving (authority-only) ---------------------------------

        /// <summary>
        /// If it is a bot seat's turn and this client is the table authority,
        /// schedule the bot's move. Chains naturally: after each bot move applies,
        /// <see cref="OnMoveAppliedChanged"/> calls this again for the next seat.
        /// </summary>
        private void ScheduleBotIfNeeded()
        {
            if (!HasMatchAuthority || _botRoutine != null)
            {
                return;
            }
            if (CurrentState == null || CurrentState.IsOver)
            {
                return;
            }
            int seat = CurrentState.CurrentPlayerIndex;
            if (!_match!.IsBotSeat(seat))
            {
                return;
            }
            _botRoutine = StartCoroutine(BotTurnRoutine(seat));
        }

        private IEnumerator BotTurnRoutine(int seat)
        {
            yield return new WaitForSeconds(BotMoveDelaySeconds);
            _botRoutine = null;

            // Re-check: state may have moved on, migrated away, or ended while we waited.
            if (!HasMatchAuthority || CurrentState == null || CurrentState.IsOver
                || CurrentState.CurrentPlayerIndex != seat || !_match!.IsBotSeat(seat))
            {
                yield break;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(CurrentState);
            if (legal.Count == 0)
            {
                yield break;
            }
            Move move = _bot.PickMove(CurrentState, legal, _botRng);
            NetworkedMove nm = move switch
            {
                PlaceMove pm => NetworkedMove.FromPlace((byte)seat, pm.Tile, pm.End),
                PassMove _ => NetworkedMove.FromPass((byte)seat),
                ResignMove _ => NetworkedMove.FromResign((byte)seat),
                _ => throw new InvalidOperationException($"Unsupported bot move: {move.GetType().Name}"),
            };
            _match.RPC_SubmitMove(nm);
        }

        /// <summary>
        /// Hard-stop the runner and tear down the controller. Used by the
        /// Back-to-lobby flow. Safe to call multiple times.
        /// </summary>
        public void ShutdownAndReturnToLobby()
        {
            if (_runner != null && _runner.IsRunning)
            {
                _ = _runner.Shutdown();
            }
        }

        /// <summary>
        /// Fallback seed derived from a high-resolution clock. Used ONLY when the
        /// server-issued seed can't be fetched (e.g. the startMatch function
        /// isn't deployed yet). A fallback round carries an empty match id and so
        /// can't settle — M4.3 rejects it. Not for competitive integrity: a
        /// client-chosen seed lets a malicious host reroll its own hand, which is
        /// exactly what the server seed prevents (ADR 0007).
        /// </summary>
        private static ulong FallbackSeed()
        {
            return unchecked((ulong)DateTime.UtcNow.Ticks);
        }

        /// <summary>
        /// Host init: fetch a server-issued seed FIRST, then spawn the match with
        /// it. Fetching before the spawn means that by the time any joiner can
        /// register, the seed and match id are already set — no gating on a
        /// late-arriving seed. On failure, degrades to a local fallback seed so
        /// online play still works before the function is deployed.
        /// </summary>
        private async Task SpawnAsHostAsync()
        {
            ulong seed;
            string matchId;
            try
            {
                MatchService.IssuedSeed issued = await MatchService.StartMatch(_targetPlayerCount, _targetMode);
                seed = issued.Seed;
                matchId = issued.MatchId;
            }
            catch (Exception e)
            {
                seed = FallbackSeed();
                matchId = string.Empty;
                Debug.LogWarning(
                    "[OnlineMatchController] startMatch failed — using a LOCAL fallback seed. " +
                    "This round cannot be settled (no server seed). " +
                    $"{e.GetType().Name}: {e.Message}");
            }

            // The runner may have been torn down while we awaited (back-to-lobby).
            if (_runner == null || !_runner.IsRunning)
            {
                return;
            }

            NetworkObject obj = _runner.Spawn(_matchPrefab, inputAuthority: _runner.LocalPlayer);
            _match = obj.GetComponent<NetworkedMatch>();
            _match.Seed = seed;
            _match.MatchId = matchId;
            _match.GameMode = _targetMode;
            _match.SeriesFormat = _targetFormat;
            _match.WinnerSeat = -1;
            _match.PlayerCount = _targetPlayerCount;
            // Seat the host at index 0, keyed by its own PlayerRef.
            _match.PlayerIds.Set(0, _localPlayerId);
            _match.SeatPlayerRefs.Set(0, _runner.LocalPlayer.PlayerId);
            _match.RecordSeatUid(0, _localUid);
            _match.RegisteredCount = 1;
            // Open the fill window: if the table hasn't filled with humans by the
            // deadline, the authority deals with bots in the empty seats.
            _match.ArmAutoStart(AutoStartSeconds);
            Debug.Log(
                $"[OnlineMatchController] Spawned as HOST. seed={seed}, match={matchId}, " +
                $"count={_targetPlayerCount}, player0={_localPlayerId}");
        }

        private void OnNetworkedMatchSpawned(NetworkedMatch match)
        {
            if (_match != null && _match == match)
            {
                return; // already wired (host's own spawn callback)
            }
            _match = match;
            _match.DealReadyChanged += OnDealReady;
            _match.RoundStartedChanged += OnRoundStarted;
            _match.RematchVotesChanged += OnRematchVotesChanged;
            _match.RegisteredCountChanged += OnRegisteredCountChanged;
            _match.MoveAppliedChanged += OnMoveAppliedChanged;
            _match.SeriesChanged += OnSeriesChanged;
            _match.MatchOverChanged += OnMatchOverChanged;

            if (!_match.Object.HasStateAuthority)
            {
                // We're a joining client — claim a seat on the host.
                Debug.Log(
                    $"[OnlineMatchController] Detected host match, registering as \"{_localPlayerId}\".");
                _match.RPC_RegisterPlayer(_localPlayerId, _localUid);
            }

            // Host case (HasStateAuthority): we already seated ourselves and set
            // Seed / MatchId / PlayerCount in SpawnAsHostAsync. We just wait for
            // seats to fill.
        }

        private void OnDealReady()
        {
            MatchState? state = DealCurrentRound();
            if (state == null)
            {
                return;
            }

            // Install the host-side move validator now rather than in
            // OnNetworkedMatchSpawned: it needs a CurrentState to validate
            // against, and no move can be submitted before the first deal lands.
            if (_match!.Object.HasStateAuthority)
            {
                _match.MoveValidator = ValidateNetworkedMove;
                // Start the score series for a Cut-Throat game (M5).
                if (IsSeries && _series == null)
                {
                    _series = SeriesState.New(state.Players, _match.SeriesFormat);
                }
            }

            _botRng = new SeededRandomSource(_match.Seed);
            MatchDealt?.Invoke(state);
            ScheduleBotIfNeeded();
        }

        private void OnRoundStarted()
        {
            MatchState? state = DealCurrentRound();
            if (state == null)
            {
                return;
            }

            _botRng = new SeededRandomSource(_match!.Seed);
            RoundStarted?.Invoke(state);
            ScheduleBotIfNeeded();
        }

        private void OnRematchVotesChanged()
        {
            RematchVotesChanged?.Invoke();

            // Host drives the re-deal: once every seat has voted, fetch a fresh
            // server seed and advance the round. Done here (not inside the RPC)
            // because the seed fetch is asynchronous. The guard prevents a second
            // fetch while the first is in flight.
            if (_match != null
                && _match.Object.HasStateAuthority
                && _match.AllRematchVotesIn
                && !_advancingRematch)
            {
                _advancingRematch = true;
                _ = AdvanceRematchAsync();
            }
        }

        private async Task AdvanceRematchAsync()
        {
            await AdvanceToNextRoundAsync();
            _advancingRematch = false;
        }

        /// <summary>
        /// Fetches a fresh server seed and advances to the next round on the
        /// authority. Shared by the Partner/private rematch vote and the Cut-Throat
        /// series auto-advance.
        /// </summary>
        private async Task AdvanceToNextRoundAsync()
        {
            // Use the round's ACTUAL player count (a short-start may have trimmed
            // it below the original target) so the recorded match matches the deal.
            int count = _match?.PlayerCount ?? _targetPlayerCount;
            GameMode mode = _match?.GameMode ?? _targetMode;
            ulong seed;
            string matchId;
            try
            {
                MatchService.IssuedSeed issued = await MatchService.StartMatch(count, mode);
                seed = issued.Seed;
                matchId = issued.MatchId;
            }
            catch (Exception e)
            {
                seed = FallbackSeed();
                matchId = string.Empty;
                Debug.LogWarning(
                    "[OnlineMatchController] startMatch (advance) failed — using a LOCAL fallback seed. " +
                    $"{e.GetType().Name}: {e.Message}");
            }

            if (_match != null && _match.Object != null && _match.Object.HasStateAuthority)
            {
                _match.AdvanceRound(seed, matchId);
            }
        }

        private void OnSeriesChanged() => SeriesChanged?.Invoke();

        private void OnMatchOverChanged()
        {
            if (MatchIsOver)
            {
                MatchEnded?.Invoke();
            }
        }

        /// <summary>
        /// Authority-side: when a Cut-Throat round ends, fold its result into the
        /// series (winner +1000), publish the new scores, and either end the match
        /// (a winner emerged) or auto-advance to the next round after a short beat.
        /// One update per round. No-op off the authority or for non-series games.
        /// </summary>
        private void ProcessSeriesRoundEndIfNeeded()
        {
            if (!HasMatchAuthority || !IsSeries || _series == null || _seriesRoundProcessed)
            {
                return;
            }
            if (CurrentState == null || !CurrentState.IsOver)
            {
                return;
            }
            MatchOutcome? outcome = _rules.GetOutcome(CurrentState);
            if (outcome == null)
            {
                return;
            }

            _seriesRoundProcessed = true;
            _series = _series.ApplyRound(outcome);

            int[] points = new int[CurrentState.Players.Count];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = _series.PointsOf(CurrentState.Players[i]);
            }

            bool over = _series.IsOver;
            int winnerSeat = -1;
            if (over && _series.Winner is PlayerId winner)
            {
                for (int i = 0; i < CurrentState.Players.Count; i++)
                {
                    if (CurrentState.Players[i].Equals(winner))
                    {
                        winnerSeat = i;
                        break;
                    }
                }
            }

            _match!.RecordSeriesResult(points, over, winnerSeat);

            if (!over)
            {
                if (_advanceRoutine != null)
                {
                    StopCoroutine(_advanceRoutine);
                }
                _advanceRoutine = StartCoroutine(AutoAdvanceRoutine());
            }
        }

        private IEnumerator AutoAdvanceRoutine()
        {
            yield return new WaitForSeconds(SeriesAdvanceDelaySeconds);
            _advanceRoutine = null;
            // The authority may have migrated or the match may have ended while we waited.
            if (!HasMatchAuthority || MatchIsOver)
            {
                yield break;
            }
            _ = AdvanceToNextRoundAsync();
        }

        private void OnRegisteredCountChanged()
        {
            WaitingChanged?.Invoke();
        }

        /// <summary>
        /// Re-derives the local <see cref="MatchState"/> from the currently
        /// replicated seed, player count and names. Used for both the first deal
        /// and every rematch — the inputs are the only thing that changed.
        /// Returns null if the match object went away or the seats aren't ready.
        /// </summary>
        private MatchState? DealCurrentRound()
        {
            if (_match == null || _runner == null)
            {
                return null;
            }

            int count = _match.PlayerCount;
            if (count < 2 || count > NetworkedMatch.MaxPlayers)
            {
                Debug.LogError($"[OnlineMatchController] Cannot deal — bad player count {count}.");
                return null;
            }

            ulong seed = _match.Seed;
            PlayerId[] players = new PlayerId[count];
            for (int i = 0; i < count; i++)
            {
                players[i] = new PlayerId(_match.PlayerIds.Get(i).ToString());
            }

            Debug.Log(
                $"[OnlineMatchController] Dealing round {_match.RoundNumber} — " +
                $"seed={seed}, players=[{string.Join(", ", players)}]");

            GameMode mode = _match.GameMode;
            Partnership partnership;
            if (mode == GameMode.Partner)
            {
                // Jamaican Partner: seats 0 & 2 vs seats 1 & 3.
                partnership = Partnership.AlternatingPairs(players[0], players[1], players[2], players[3]);
                _rules = new JamaicanPartnerRules();
            }
            else
            {
                partnership = Partnership.CutThroat(players);
                _rules = new CutThroatRules();
            }

            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(count),
                players,
                partnership,
                new SeededRandomSource(seed));

            CurrentState = state;
            _settlementSubmitted = false; // fresh round — allow one settlement submit
            _seriesRoundProcessed = false; // allow one series-score update this round
            LocalPlayerIndex = FindLocalSeat(count);
            if (LocalPlayerIndex < 0)
            {
                Debug.LogError("[OnlineMatchController] Local player has no seat in the dealt round.");
                return null;
            }
            LocalPlayer = state.Players[LocalPlayerIndex];

            LogDeal(state);
            return state;
        }

        /// <summary>
        /// Finds this client's seat by matching the local <see cref="PlayerRef"/>
        /// against the seat owners the host recorded — robust against duplicate
        /// display names.
        /// </summary>
        private int FindLocalSeat(int count)
        {
            int localRef = _runner!.LocalPlayer.PlayerId;
            for (int i = 0; i < count; i++)
            {
                if (_match!.SeatPlayerRefs.Get(i) == localRef)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Opts the local player into a rematch of the finished round. Both
        /// players must call this before the host re-deals, so a single tap
        /// never restarts the match under the opponent. Returns false when a
        /// rematch isn't offerable — round still running, opponent already
        /// gone, or we already voted.
        /// </summary>
        public bool TryRequestRematch()
        {
            if (_match == null || CurrentState == null || LocalPlayerIndex < 0)
            {
                return false;
            }
            if (!CurrentState.IsOver || _opponentLeftFired || LocalWantsRematch)
            {
                return false;
            }

            _match.RPC_RequestRematch((byte)LocalPlayerIndex);
            return true;
        }

        private bool ReadRematchVote(int playerIndex)
        {
            if (_match == null || playerIndex < 0 || playerIndex >= _match.PlayerCount)
            {
                return false;
            }
            return (_match.RematchVoteMask & (1 << playerIndex)) != 0;
        }

        /// <summary>
        /// Host-side validator passed to <see cref="NetworkedMatch.MoveValidator"/>.
        /// Decodes the wire form and runs Pose.Core's <c>IsLegal</c> against
        /// the live <see cref="CurrentState"/>.
        /// </summary>
        private bool ValidateNetworkedMove(NetworkedMove nm)
        {
            if (CurrentState == null)
            {
                return false;
            }
            try
            {
                Move m = nm.ToCoreMove(CurrentState.Players);
                return _rules.IsLegal(CurrentState, m);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OnlineMatchController] validator exception: {e.Message}");
                return false;
            }
        }

        private void OnMoveAppliedChanged(int from, int to)
        {
            if (_match == null || CurrentState == null)
            {
                return;
            }
            for (int i = from; i < to; i++)
            {
                NetworkedMove nm = _match.Moves.Get(i);
                Move move;
                try
                {
                    move = nm.ToCoreMove(CurrentState.Players);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[OnlineMatchController] move {i} decode failed: {e.Message}");
                    continue;
                }

                try
                {
                    CurrentState = _rules.Apply(CurrentState, move);
                    Debug.Log($"[OnlineMatch] move {i} applied: {move}");
                    MoveApplied?.Invoke(CurrentState, move);
                }
                catch (Exception e)
                {
                    // Should never happen if the host validator agreed — but if
                    // it does (e.g. clients diverged), we log loudly rather than
                    // silently desync further.
                    Debug.LogError(
                        $"[OnlineMatchController] move {i} apply failed " +
                        $"(possible desync): {e.Message}");
                }
            }

            TrySubmitSettlement();
            ProcessSeriesRoundEndIfNeeded();
            ScheduleBotIfNeeded();
        }

        /// <summary>
        /// When the round finishes, the host submits the move log for server-side
        /// settlement (M4.3). Only the host submits (the one seat the server can
        /// bind to the match), only once per round, and only for a server-issued
        /// match (a fallback round has no match id and can't settle).
        /// </summary>
        private void TrySubmitSettlement()
        {
            if (_match == null || CurrentState == null || !CurrentState.IsOver || _settlementSubmitted)
            {
                return;
            }
            if (!_match.Object.HasStateAuthority)
            {
                return; // only the host submits
            }
            string matchId = _match.MatchId.ToString();
            if (string.IsNullOrEmpty(matchId))
            {
                Debug.LogWarning("[OnlineMatchController] Round has no server match id — skipping settlement.");
                return;
            }

            _settlementSubmitted = true;

            List<string> players = new(CurrentState.Players.Count);
            foreach (PlayerId p in CurrentState.Players)
            {
                players.Add(p.Value);
            }
            string[] seatUids = _match.HostSeatUids();
            List<NetworkedMove> moves = new(_match.MoveCount);
            for (int i = 0; i < _match.MoveCount; i++)
            {
                moves.Add(_match.Moves.Get(i));
            }

            _ = SubmitSettlementAsync(matchId, players, seatUids, moves);
        }

        private static async Task SubmitSettlementAsync(
            string matchId,
            List<string> players,
            IReadOnlyList<string> seatUids,
            List<NetworkedMove> moves)
        {
            try
            {
                await SettlementService.SubmitRoundLog(matchId, players, seatUids, moves);
                Debug.Log($"[OnlineMatchController] Settlement submitted for match {matchId}.");
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[OnlineMatchController] Settlement submit failed for {matchId}: " +
                    $"{e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Called by the local UI when the human picks a tile or presses pass.
        /// Validates locally to avoid sending obviously bogus RPCs, encodes
        /// the move, and fires <see cref="NetworkedMatch.RPC_SubmitMove"/>.
        /// Returns false if the move is rejected client-side — the UI should
        /// keep the tile in the player's hand. A true return means the RPC
        /// was sent; the move will only show up locally once it loops back
        /// through <see cref="OnMoveAppliedChanged"/>.
        /// </summary>
        public bool TrySubmitLocalMove(Move move)
        {
            if (_match == null || CurrentState == null || LocalPlayer == null)
            {
                return false;
            }
            if (!move.Player.Equals(LocalPlayer.Value))
            {
                Debug.LogWarning(
                    $"[OnlineMatchController] move's player is {move.Player} " +
                    $"but LocalPlayer is {LocalPlayer} — rejected.");
                return false;
            }
            // Place/Pass require it's your turn; Resign is unilateral and can
            // fire off-turn (rule engine accepts it from any participant).
            if (move is not ResignMove
                && !CurrentState.CurrentPlayer.Equals(LocalPlayer.Value))
            {
                return false;
            }
            if (!_rules.IsLegal(CurrentState, move))
            {
                return false;
            }

            NetworkedMove nm = move switch
            {
                PlaceMove pm => NetworkedMove.FromPlace((byte)LocalPlayerIndex, pm.Tile, pm.End),
                PassMove _ => NetworkedMove.FromPass((byte)LocalPlayerIndex),
                ResignMove _ => NetworkedMove.FromResign((byte)LocalPlayerIndex),
                _ => throw new InvalidOperationException($"Unsupported move type: {move.GetType().Name}"),
            };
            _match.RPC_SubmitMove(nm);
            return true;
        }

        /// <summary>
        /// Submits a Resign for the local player. Wraps <see cref="TrySubmitLocalMove"/>
        /// — useful for the UI's resign button which doesn't need to manufacture
        /// a Move first.
        /// </summary>
        public bool TrySubmitLocalResign()
        {
            if (LocalPlayer == null)
            {
                return false;
            }
            return TrySubmitLocalMove(new ResignMove(LocalPlayer.Value));
        }

        private static void LogDeal(MatchState state)
        {
            Debug.Log($"  starting player: {state.CurrentPlayer.Value}");
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                Debug.Log($"  {p.Value} hand: {string.Join(" ", state.Hands[p])}");
            }
        }
    }
}
