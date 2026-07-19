#nullable enable
using System;
using Fusion;
using Pose.Core;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Fusion <see cref="NetworkBehaviour"/> that synchronises the minimal
    /// inputs needed to derive a 2-player Cut-Throat round: the deal seed,
    /// both players' display names (M3.3), and the append-only move log
    /// (M3.4). Both clients run the deterministic Pose.Core engine against
    /// these inputs and get the same <c>MatchState</c> at every turn (proven
    /// by Pose.Core.Tests.MoveReplaySpec). State stays local — we don't push
    /// the full MatchState over the wire, only what's needed to reconstruct
    /// it.
    ///
    /// Authority model (Fusion Shared mode):
    /// - Host (first player in the room) spawns this object → becomes
    ///   <see cref="Object"/>.StateAuthority. Writes <see cref="Seed"/> and
    ///   <see cref="Player1Id"/>.
    /// - Client (second player) sees the spawn via <see cref="Spawned"/>, then
    ///   sends their display name to the host via
    ///   <see cref="RPC_RegisterPlayer2"/>. Host writes <see cref="Player2Id"/>,
    ///   sets <see cref="RoundNumber"/> to 1, and flips <see cref="DealReady"/>.
    /// - Once dealing is done, either client submits moves via
    ///   <see cref="RPC_SubmitMove"/>. Host validates with Pose.Core's
    ///   IsLegal (installed as <see cref="MoveValidator"/> by the controller),
    ///   appends to <see cref="Moves"/>, and bumps <see cref="MoveCount"/>.
    ///
    /// Rematch (M3.6). <see cref="DealReady"/> latches true for the lifetime of
    /// the match and means "both players registered". Which round is current is
    /// carried by <see cref="RoundNumber"/>. Both players must opt in — each
    /// calls <see cref="RPC_RequestRematch"/>, the host records their vote, and
    /// only when both are in does it re-seed, truncate the move log, and
    /// increment the round. See <c>docs/DECISIONS/0005-in-place-online-rematch.md</c>.
    ///
    /// All edge detection over the replicated fields (deal landed / round
    /// advanced / which move indices are new) is delegated to the Unity-free
    /// <see cref="MatchSignalTracker"/> so it can be unit-tested without Fusion.
    /// This matters because a rematch resets <see cref="MoveCount"/> to zero,
    /// which a naive monotonic detector would swallow.
    /// </summary>
    public sealed class NetworkedMatch : NetworkBehaviour
    {
        /// <summary>
        /// Maximum moves that can fit in the networked log. A double-six 2P
        /// round is bounded above by 28 placements plus a handful of passes;
        /// 64 leaves comfortable headroom and stays a power of two for
        /// Fusion's NetworkArray layout. The log is per-round — a rematch
        /// truncates it — so this bound is not cumulative across rounds.
        /// </summary>
        public const int MaxMoves = 64;

        private const byte HostPlayerIndex = 0;
        private const byte JoinerPlayerIndex = 1;

        /// <summary>
        /// Static event raised every time a NetworkedMatch's <c>Spawned()</c>
        /// fires (on host AND client). Used by <see cref="OnlineMatchController"/>
        /// to discover the network object without polling the scene.
        /// </summary>
        public static event Action<NetworkedMatch>? AnySpawned;

        /// <summary>
        /// Fires once, when the initial deal becomes available on this client.
        /// Listener should read the networked fields and run <c>Dealer.Deal</c>.
        /// </summary>
        public event Action? DealReadyChanged;

        /// <summary>
        /// Fires when <see cref="RoundNumber"/> advances — i.e. a rematch was
        /// agreed and the host published a fresh <see cref="Seed"/>. Listener
        /// re-deals from the new seed and discards the previous round's state.
        /// </summary>
        public event Action? RoundStartedChanged;

        /// <summary>
        /// Fires whenever either rematch vote changes, so the UI can reflect
        /// "you asked" / "your opponent asked" without polling.
        /// </summary>
        public event Action? RematchVotesChanged;

        /// <summary>
        /// Fires on each client whenever the move log advances, passing the new
        /// (zero-based) index range the listener should pull from
        /// <see cref="Moves"/> and apply to its local MatchState. Range
        /// semantics are [from, to) — iterate
        /// <c>for (int i = from; i &lt; to; i++)</c>. Indices are relative to
        /// the current round.
        /// </summary>
        public event Action<int, int>? MoveAppliedChanged;

        [Networked] public ulong Seed { get; set; }
        [Networked] public NetworkString<_32> Player1Id { get; set; }
        [Networked] public NetworkString<_32> Player2Id { get; set; }
        [Networked] public bool DealReady { get; set; }
        [Networked, Capacity(MaxMoves)] public NetworkArray<NetworkedMove> Moves => default;
        [Networked] public int MoveCount { get; set; }

        /// <summary>
        /// 0 before the first deal, 1 for the first round, incrementing once per
        /// agreed rematch. Paired with a fresh <see cref="Seed"/> and a
        /// <see cref="MoveCount"/> of 0 in the same replicated snapshot.
        /// </summary>
        [Networked] public int RoundNumber { get; set; }

        /// <summary>Host's standing rematch vote for the finished round.</summary>
        [Networked] public bool Player1WantsRematch { get; set; }

        /// <summary>Joiner's standing rematch vote for the finished round.</summary>
        [Networked] public bool Player2WantsRematch { get; set; }

        /// <summary>
        /// Set by the host's <see cref="OnlineMatchController"/> at spawn time
        /// so <see cref="RPC_SubmitMove"/> can validate against the current
        /// authoritative state before appending. The signature is a
        /// callback-on-NetworkedMove rather than a direct Pose.Core dependency
        /// so this file doesn't drag in the rule engine.
        /// </summary>
        public Func<NetworkedMove, bool>? MoveValidator { get; set; }

        /// <summary>
        /// Supplies the seed for each rematch round. Installed by the host's
        /// <see cref="OnlineMatchController"/> for the same reason as
        /// <see cref="MoveValidator"/> — seed policy is the controller's
        /// business, not the transport's. M4 replaces the local implementation
        /// with a server-issued seed without touching this class.
        /// </summary>
        public Func<ulong>? NextSeedProvider { get; set; }

        private readonly MatchSignalTracker _signals = new();

        private bool _lastPlayer1WantsRematch;
        private bool _lastPlayer2WantsRematch;

        public override void Spawned()
        {
            base.Spawned();
            Debug.Log(
                $"[NetworkedMatch] Spawned (authority={Object.HasStateAuthority}, " +
                $"seed={Seed}, p1={Player1Id}, p2={Player2Id})");
            AnySpawned?.Invoke(this);
        }

        public override void Render()
        {
            MatchSignal signal = _signals.Observe(DealReady, RoundNumber, MoveCount);

            // Deal/round first, moves second: the listener must have a
            // MatchState to apply moves onto. DealStarted and RoundStarted are
            // mutually exclusive by construction.
            if (signal.DealStarted)
            {
                DealReadyChanged?.Invoke();
            }
            if (signal.RoundStarted)
            {
                RoundStartedChanged?.Invoke();
            }
            if (signal.HasMoves)
            {
                MoveAppliedChanged?.Invoke(signal.MovesFrom, signal.MovesTo);
            }

            if (Player1WantsRematch != _lastPlayer1WantsRematch
                || Player2WantsRematch != _lastPlayer2WantsRematch)
            {
                _lastPlayer1WantsRematch = Player1WantsRematch;
                _lastPlayer2WantsRematch = Player2WantsRematch;
                RematchVotesChanged?.Invoke();
            }
        }

        /// <summary>
        /// Called by the joining client to register their display name on the
        /// host. Source = All so the joiner (who isn't the StateAuthority) can
        /// invoke it; target = StateAuthority so only the host actually writes
        /// the networked fields. Setting <see cref="DealReady"/> here triggers
        /// both clients' <see cref="Render"/> edge detection.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RegisterPlayer2(string playerId, RpcInfo info = default)
        {
            // Defensive: only the StateAuthority should actually write here.
            // Fusion's RpcTargets.StateAuthority routing guarantees this in
            // normal flow, but the explicit check guards against tooling
            // sending the RPC to non-authority instances.
            if (!Object.HasStateAuthority)
            {
                return;
            }
            if (DealReady)
            {
                // A duplicate registration (retried RPC, reconnect) must not
                // re-latch the deal or clobber Player2Id mid-match.
                Debug.LogWarning(
                    "[NetworkedMatch] RPC_RegisterPlayer2 ignored — deal already ready.");
                return;
            }
            Player2Id = playerId;
            RoundNumber = 1;
            DealReady = true;
            Debug.Log($"[NetworkedMatch] RPC: Player2 registered as {playerId}");
        }

        /// <summary>
        /// Either client calls this with their intended move. Host validates
        /// via <see cref="MoveValidator"/> (installed by the controller) and,
        /// if legal, appends to <see cref="Moves"/> and bumps
        /// <see cref="MoveCount"/>. Source = All because both joiner and host
        /// submit their own moves; target = StateAuthority so only the host
        /// mutates the log.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SubmitMove(NetworkedMove move, RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }
            if (MoveCount >= MaxMoves)
            {
                Debug.LogWarning(
                    $"[NetworkedMatch] RPC_SubmitMove dropped — log full at {MoveCount}.");
                return;
            }
            if (MoveValidator == null)
            {
                Debug.LogWarning(
                    "[NetworkedMatch] RPC_SubmitMove dropped — host has no MoveValidator " +
                    "installed yet (controller not ready).");
                return;
            }
            if (!MoveValidator(move))
            {
                Debug.LogWarning(
                    $"[NetworkedMatch] RPC_SubmitMove rejected as illegal: " +
                    $"player={move.PlayerIndex} kind={move.Kind} " +
                    $"tile=[{move.LowPip}|{move.HighPip}] end={move.EndSide}");
                return;
            }
            Moves.Set(MoveCount, move);
            MoveCount++;
        }

        /// <summary>
        /// Either client calls this to opt into a rematch of the finished round.
        /// The host records the vote; the round only advances once both players
        /// have voted, so neither player can restart the match under the other.
        /// Votes are cleared as part of advancing, and are idempotent — a
        /// double-tap re-asserts the same vote rather than toggling it off.
        /// </summary>
        /// <param name="playerIndex">0 for the host, 1 for the joiner.</param>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestRematch(byte playerIndex, RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }
            if (!DealReady)
            {
                Debug.LogWarning(
                    "[NetworkedMatch] RPC_RequestRematch dropped — no deal yet.");
                return;
            }

            switch (playerIndex)
            {
                case HostPlayerIndex:
                    Player1WantsRematch = true;
                    break;
                case JoinerPlayerIndex:
                    Player2WantsRematch = true;
                    break;
                default:
                    Debug.LogWarning(
                        $"[NetworkedMatch] RPC_RequestRematch dropped — bad " +
                        $"playerIndex {playerIndex}.");
                    return;
            }

            Debug.Log(
                $"[NetworkedMatch] Rematch vote from player {playerIndex} " +
                $"(p1={Player1WantsRematch}, p2={Player2WantsRematch})");

            if (Player1WantsRematch && Player2WantsRematch)
            {
                StartNextRound();
            }
        }

        /// <summary>
        /// Host-only. Publishes a fresh seed, truncates the move log, clears the
        /// rematch votes and advances the round — all within one tick, so every
        /// client observes them together and re-deals against a consistent
        /// snapshot.
        /// </summary>
        private void StartNextRound()
        {
            if (NextSeedProvider == null)
            {
                Debug.LogWarning(
                    "[NetworkedMatch] Rematch stalled — host has no NextSeedProvider " +
                    "installed. Votes retained; will advance once it is.");
                return;
            }

            Seed = NextSeedProvider();
            MoveCount = 0;
            Player1WantsRematch = false;
            Player2WantsRematch = false;
            RoundNumber++;

            Debug.Log($"[NetworkedMatch] Rematch agreed — round {RoundNumber}, seed={Seed}");
        }
    }
}
