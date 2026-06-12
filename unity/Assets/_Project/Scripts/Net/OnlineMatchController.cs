#nullable enable
using System;
using System.Linq;
using Fusion;
using Pose.Core;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Orchestrates the 2-player online Cut-Throat deal. Created by
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
    /// Both sides subscribe to the NetworkedMatch's <c>DealReadyChanged</c>
    /// event. When it fires, both clients run <c>Dealer.Deal</c> locally with
    /// the synced inputs and log the resulting hands — verification that the
    /// deterministic engine produced identical state on both ends.
    /// </summary>
    public sealed class OnlineMatchController : MonoBehaviour
    {
        public event Action<MatchState>? MatchDealt;

        private NetworkObject? _matchPrefab;
        private NetworkRunner? _runner;
        private string _localPlayerId = string.Empty;
        private NetworkedMatch? _match;

        public void Setup(NetworkObject matchPrefab, NetworkRunner runner, string localPlayerId)
        {
            _matchPrefab = matchPrefab;
            _runner = runner;
            _localPlayerId = string.IsNullOrEmpty(localPlayerId) ? "anon" : localPlayerId;

            NetworkedMatch.AnySpawned += OnNetworkedMatchSpawned;

            // Catch the host-already-spawned case (e.g. we joined an existing
            // room where the host's NetworkedMatch landed before our subscribe).
            // FindObjectOfType is OK at one-shot init time.
            NetworkedMatch existing = UnityEngine.Object.FindObjectOfType<NetworkedMatch>();
            if (existing != null)
            {
                OnNetworkedMatchSpawned(existing);
                return;
            }

            // No existing match → we're the first one in. Spawn as host.
            if (_runner.ActivePlayers.Count() <= 1)
            {
                SpawnAsHost();
            }
            // Otherwise we're a joiner but the host's NetworkedMatch hasn't
            // arrived yet — the AnySpawned subscription will catch it.
        }

        private void OnDestroy()
        {
            NetworkedMatch.AnySpawned -= OnNetworkedMatchSpawned;
            if (_match != null)
            {
                _match.DealReadyChanged -= OnDealReady;
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

            LogDeal(state);
            MatchDealt?.Invoke(state);
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
