#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Pose.Core;
using UnityEngine;

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
        private readonly CutThroatRules _rules = new();

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

        private NetworkObject? _matchPrefab;
        private NetworkRunner? _runner;
        private string _localPlayerId = string.Empty;
        private string _localUid = string.Empty;
        private int _targetPlayerCount = 2;
        private NetworkedMatch? _match;

        private int _lastSeenPlayerCount;
        private bool _opponentLeftFired;
        private bool _advancingRematch;
        private bool _settlementSubmitted;

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
            int targetPlayerCount)
        {
            _matchPrefab = matchPrefab;
            _runner = runner;
            _localPlayerId = string.IsNullOrEmpty(localPlayerId) ? "anon" : localPlayerId;
            _localUid = localUid;
            _targetPlayerCount = Mathf.Clamp(targetPlayerCount, 2, NetworkedMatch.MaxPlayers);

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
                if (_match.Object != null && _match.Object.HasStateAuthority)
                {
                    _match.MoveValidator = null;
                }
            }
        }

        private void Update()
        {
            if (_runner == null || _opponentLeftFired)
            {
                return;
            }
            int count = _runner.ActivePlayers.Count();
            if (_lastSeenPlayerCount == 0)
            {
                _lastSeenPlayerCount = count;
                return;
            }
            if (count < _lastSeenPlayerCount)
            {
                _opponentLeftFired = true;
                Debug.Log(
                    $"[OnlineMatchController] OpponentLeft detected " +
                    $"(player count {_lastSeenPlayerCount} -> {count})");
                OpponentLeft?.Invoke();
            }
            _lastSeenPlayerCount = count;
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
                MatchService.IssuedSeed issued = await MatchService.StartMatch(_targetPlayerCount);
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
            _match.PlayerCount = _targetPlayerCount;
            // Seat the host at index 0, keyed by its own PlayerRef.
            _match.PlayerIds.Set(0, _localPlayerId);
            _match.SeatPlayerRefs.Set(0, _runner.LocalPlayer.PlayerId);
            _match.RecordSeatUid(0, _localUid);
            _match.RegisteredCount = 1;
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
            }

            MatchDealt?.Invoke(state);
        }

        private void OnRoundStarted()
        {
            MatchState? state = DealCurrentRound();
            if (state == null)
            {
                return;
            }

            RoundStarted?.Invoke(state);
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
            // Use the round's ACTUAL player count (a short-start may have trimmed
            // it below the original target) so the recorded match matches the deal.
            int count = _match?.PlayerCount ?? _targetPlayerCount;
            ulong seed;
            string matchId;
            try
            {
                MatchService.IssuedSeed issued = await MatchService.StartMatch(count);
                seed = issued.Seed;
                matchId = issued.MatchId;
            }
            catch (Exception e)
            {
                seed = FallbackSeed();
                matchId = string.Empty;
                Debug.LogWarning(
                    "[OnlineMatchController] startMatch (rematch) failed — using a LOCAL fallback seed. " +
                    $"{e.GetType().Name}: {e.Message}");
            }

            if (_match != null && _match.Object != null && _match.Object.HasStateAuthority)
            {
                _match.AdvanceRound(seed, matchId);
            }
            _advancingRematch = false;
        }

        private void OnRegisteredCountChanged()
        {
            WaitingChanged?.Invoke();
        }

        /// <summary>
        /// Starts the round now with whoever has joined (host only). Trims the
        /// target count to the current seat count. No-op if the deal already
        /// landed or fewer than two players are present.
        /// </summary>
        public void StartWithCurrentPlayers()
        {
            _match?.StartWithCurrentPlayers();
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

            Partnership partnership = Partnership.CutThroat(players);
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(count),
                players,
                partnership,
                new SeededRandomSource(seed));

            CurrentState = state;
            _settlementSubmitted = false; // fresh round — allow one settlement submit
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
