#nullable enable
using System;
using Fusion;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// M3.1 spike: stands up a Photon Fusion 2 <see cref="NetworkRunner"/> in
    /// shared mode and joins a single "PoseSpike" session. Goal is only to
    /// prove the SDK is installed, the AppID is valid, and Photon Cloud is
    /// reachable — no networked gameplay yet (that lands in M3.3 onward).
    ///
    /// Singleton with <c>DontDestroyOnLoad</c>, mirroring
    /// <see cref="FirebaseBootstrap"/> / <see cref="ProfileService"/> /
    /// <see cref="StatsService"/>. Caller flow in M3.1: BoardBootstrap chains
    /// auth → profile → photon, fail-open on each (game still plays offline).
    /// </summary>
    public sealed class PhotonBootstrap : MonoBehaviour
    {
        private const string SpikeSessionName = "PoseSpike";

        public static PhotonBootstrap? Instance { get; private set; }

        public bool IsConnected { get; private set; }
        public bool HasFailed { get; private set; }
        public string? ErrorMessage { get; private set; }

        public event Action? Connected;
        public event Action<string>? Failed;

        private NetworkRunner? _runner;

        private async void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                _runner = gameObject.AddComponent<NetworkRunner>();
                // Shared mode: peers gossip state, no host/server distinction.
                // Simplest fit for the spike; M3 may switch to a host model
                // later if the trust boundary makes it necessary.
                StartGameArgs args = new()
                {
                    GameMode = GameMode.Shared,
                    SessionName = SpikeSessionName,
                };

                StartGameResult result = await _runner.StartGame(args);

                if (result.Ok)
                {
                    IsConnected = true;
                    Debug.Log(
                        $"[PhotonBootstrap] Connected to Photon Cloud, session: " +
                        $"{SpikeSessionName}");
                    Connected?.Invoke();
                }
                else
                {
                    Fail($"Photon start failed: {result.ShutdownReason} — {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Fail($"Photon init exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void Fail(string message)
        {
            HasFailed = true;
            ErrorMessage = message;
            Debug.LogError($"[PhotonBootstrap] {message}");
            Failed?.Invoke(message);
        }
    }
}
