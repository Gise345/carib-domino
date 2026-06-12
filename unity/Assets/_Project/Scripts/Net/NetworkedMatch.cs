#nullable enable
using System;
using Fusion;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Fusion <see cref="NetworkBehaviour"/> that synchronises the minimal
    /// inputs needed to derive a 2-player Cut-Throat round: the deal seed and
    /// both players' display names. Both clients run the deterministic
    /// <c>Pose.Core.Dealer.Deal</c> against these inputs and get the same
    /// <c>MatchState</c> bit-for-bit (proven by M1 step 3's replay-determinism
    /// tests). State stays local — we don't push the full MatchState over the
    /// wire, only what's needed to reconstruct it.
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
    /// </summary>
    public sealed class NetworkedMatch : NetworkBehaviour
    {
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

        [Networked] public ulong Seed { get; set; }
        [Networked] public NetworkString<_32> Player1Id { get; set; }
        [Networked] public NetworkString<_32> Player2Id { get; set; }
        [Networked] public bool DealReady { get; set; }

        private bool _lastDealReady;

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
    }
}
