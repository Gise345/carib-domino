#nullable enable
using System;
using System.Linq;
using Fusion;
using Pose.Core;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Orchestrates the 2-player online Cut-Throat round. Created by
    /// <see cref="Pose.Game.BoardBootstrap"/> once the lobby reports an
    /// online room is active. On <see cref="Setup"/>:
    /// <list type="bullet">
    ///   <item>If we're the only player in the session → we're host. Spawn
    ///         <see cref="NetworkedMatch"/> with a fresh seed and our display
    ///         name in <c>Player1Id</c>.</item>
    ///   <item>If a NetworkedMatch already exists (host spawned it before we
    ///         joined) → we're client. Send our display name to the host via
    ///         <see cref="NetworkedMatch.RPC_RegisterPlayer2"/>.</item>
    /// </list>
    /// Both sides subscribe to <see cref="NetworkedMatch.DealReadyChanged"/>;
    /// when it fires, each client runs <c>Dealer.Deal</c> locally against the
    /// synced inputs and gets the same <see cref="MatchState"/> (M3.3).
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

        /// <summary>This client's index into <c>CurrentState.Players</c> (0 host, 1 joiner).</summary>
        public int LocalPlayerIndex { get; private set; } = -1;

        private NetworkObject? _matchPrefab;
        private NetworkRunner? _runner;
        private string _localPlayerId = string.Empty;
        private NetworkedMatch? _match;

        private int _lastSeenPlayerCount;
        private bool _opponentLeftFired;

        public void Setup(NetworkObject matchPrefab, NetworkRunner runner, string localPlayerId)
        {
            _matchPrefab = matchPrefab;
            _runner = runner;
            _localPlayerId = string.IsNullOrEmpty(localPlayerId) ? "anon" : localPlayerId;

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
                SpawnAsHost();
            }
        }

        private void OnDestroy()
        {
            NetworkedMatch.AnySpawned -= OnNetworkedMatchSpawned;
            if (_match != null)
            {
                _match.DealReadyChanged -= OnDealReady;
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

        private void SpawnAsHost()
        {
            // Seed derived from a high-resolution clock — different per session,
            // deterministic-enough for the spike. M4's settlement validator will
            // replace this with a server-issued seed for trust-boundary reasons.
            ulong seed = unchecked((ulong)DateTime.UtcNow.Ticks);

            NetworkObject obj = _runner!.Spawn(
                _matchPrefab,
                inputAuthority: _runner.LocalPlayer);
            _match = obj.GetComponent<NetworkedMatch>();
            _match.Seed = seed;
            _match.Player1Id = _localPlayerId;
            Debug.Log(
                $"[OnlineMatchController] Spawned as HOST. " +
                $"seed={seed}, player1={_localPlayerId}");
        }

        private void OnNetworkedMatchSpawned(NetworkedMatch match)
        {
            if (_match != null && _match == match)
            {
                return; // already wired (host's own spawn callback)
            }
            _match = match;
            _match.DealReadyChanged += OnDealReady;
            _match.MoveAppliedChanged += OnMoveAppliedChanged;

            if (!_match.Object.HasStateAuthority)
            {
                // We're the joining client — tell the host our display name.
                Debug.Log(
                    $"[OnlineMatchController] Detected host match, joining as " +
                    $"Player2 with id={_localPlayerId}");
                _match.RPC_RegisterPlayer2(_localPlayerId);
            }

            // Host case (HasStateAuthority): we already set Player1Id / Seed in
            // SpawnAsHost. We just wait for the joiner's RPC to flip DealReady.
        }

        private void OnDealReady()
        {
            if (_match == null)
            {
                return;
            }

            string p1 = _match.Player1Id.ToString();
            string p2 = _match.Player2Id.ToString();
            ulong seed = _match.Seed;

            Debug.Log(
                $"[OnlineMatchController] Deal ready — seed={seed}, " +
                $"p1=\"{p1}\", p2=\"{p2}\"");

            PlayerId[] players = { new(p1), new(p2) };
            Partnership partnership = Partnership.CutThroat(players);
            MatchState state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(2),
                players,
                partnership,
                new SeededRandomSource(seed));

            CurrentState = state;
            LocalPlayerIndex = _match.Object.HasStateAuthority ? 0 : 1;
            LocalPlayer = state.Players[LocalPlayerIndex];

            // Install the move validator on the host so RPC_SubmitMove can
            // reject illegal submissions before appending to the log. Done
            // here (not in OnNetworkedMatchSpawned) because we need
            // CurrentState to validate against.
            if (_match.Object.HasStateAuthority)
            {
                _match.MoveValidator = ValidateNetworkedMove;
            }

            LogDeal(state);
            MatchDealt?.Invoke(state);
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
