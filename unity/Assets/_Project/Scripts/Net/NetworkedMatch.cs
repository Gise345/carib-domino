#nullable enable
using System;
using Fusion;
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
    ///   <see cref="RPC_RegisterPlayer2"/>. Host writes <see cref="Player2Id"/>
    ///   and flips <see cref="DealReady"/> to true.
    /// - Both clients poll <see cref="DealReady"/> in <see cref="Render"/> and
    ///   fire <see cref="DealReadyChanged"/> on the rising edge.
    /// - Once dealing is done, either client submits moves via
    ///   <see cref="RPC_SubmitMove"/>. Host validates with Pose.Core's
    ///   IsLegal (installed as <see cref="MoveValidator"/> by the controller),
    ///   appends to <see cref="Moves"/>, and bumps <see cref="MoveCount"/>.
    ///   Both clients watch <see cref="MoveCount"/> in <see cref="Render"/>
    ///   and fire <see cref="MoveAppliedChanged"/> for the new index range
    ///   so the controller can replay.
    /// </summary>
    public sealed class NetworkedMatch : NetworkBehaviour
    {
        /// <summary>
        /// Maximum moves that can fit in the networked log. A double-six 2P
        /// round is bounded above by 28 placements plus a handful of passes;
        /// 64 leaves comfortable headroom and stays a power of two for
        /// Fusion's NetworkArray layout.
        /// </summary>
        public const int MaxMoves = 64;

        /// <summary>
        /// Static event raised every time a NetworkedMatch's <c>Spawned()</c>
        /// fires (on host AND client). Used by <see cref="OnlineMatchController"/>
        /// to discover the network object without polling the scene.
        /// </summary>
        public static event Action<NetworkedMatch>? AnySpawned;

        /// <summary>
        /// Fires on the rising edge of <see cref="DealReady"/> on either side
        /// of the connection. Listener should read the networked fields and
        /// run <c>Dealer.Deal</c> locally.
        /// </summary>
        public event Action? DealReadyChanged;

        /// <summary>
        /// Fires on each client whenever <see cref="MoveCount"/> advances,
        /// passing the new (zero-based) index range the listener should pull
        /// from <see cref="Moves"/> and apply to its local MatchState. Range
        /// semantics are [from, to) — iterate
        /// <c>for (int i = from; i &lt; to; i++)</c>.
        /// </summary>
        public event Action<int, int>? MoveAppliedChanged;

        [Networked] public ulong Seed { get; set; }
        [Networked] public NetworkString<_32> Player1Id { get; set; }
        [Networked] public NetworkString<_32> Player2Id { get; set; }
        [Networked] public bool DealReady { get; set; }
        [Networked, Capacity(MaxMoves)] public NetworkArray<NetworkedMove> Moves => default;
        [Networked] public int MoveCount { get; set; }

        /// <summary>
        /// Set by the host's <see cref="OnlineMatchController"/> at spawn time
        /// so <see cref="RPC_SubmitMove"/> can validate against the current
        /// authoritative state before appending. The signature is a
        /// callback-on-NetworkedMove rather than a direct Pose.Core dependency
        /// so this file doesn't drag in the rule engine.
        /// </summary>
        public Func<NetworkedMove, bool>? MoveValidator { get; set; }

        private bool _lastDealReady;
        private int _lastMoveCount;

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
            // Rising-edge detection — fires DealReadyChanged once when the
            // networked flag transitions false → true on this client. Cheaper
            // than an OnChanged callback and works the same on host + joiner.
            if (DealReady && !_lastDealReady)
            {
                _lastDealReady = true;
                DealReadyChanged?.Invoke();
            }

            // Likewise for the move log — host only ever appends, so a
            // strict > check is enough. Hand the new index range to the
            // controller in one shot rather than one event per move so a
            // burst (e.g. just-joined client catching up) is a single call.
            if (MoveCount > _lastMoveCount)
            {
                int from = _lastMoveCount;
                int to = MoveCount;
                _lastMoveCount = to;
                MoveAppliedChanged?.Invoke(from, to);
            }
        }

        /// <summary>
        /// Called by the joining client to register their display name on the
        /// host. Source = All so the joiner (who isn't the StateAuthority) can
        /// invoke it; target = StateAuthority so only the host actually writes
        /// the networked fields. Setting <see cref="DealReady"/> here triggers
        /// both clients' <see cref="Render"/> rising-edge detection.
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
            Player2Id = playerId;
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
    }
}
