#nullable enable
using System;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Singleton that owns the project's Photon Fusion 2 <see cref="NetworkRunner"/>.
    ///
    /// M3.2 shape: <see cref="Awake"/> just registers the singleton — it no
    /// longer auto-connects to a hardcoded session. Callers (today: the lobby
    /// UI) trigger the connection by invoking <see cref="CreateRoom"/> or
    /// <see cref="JoinRoom"/> with a specific session name. The session name
    /// is the room code shared between two players.
    ///
    /// Fusion's Shared mode is "create-or-join" by session name — there is no
    /// strict "must already exist" semantics for the Join path in shared mode.
    /// If a player enters a code that doesn't exist, they'll silently create
    /// that session and wait alone in it. Tightening that to "join only" is a
    /// later concern (M3.6 host migration / disconnect handling, or a
    /// pre-flight session-list query).
    /// </summary>
    public sealed class PhotonBootstrap : MonoBehaviour
    {
        public static PhotonBootstrap? Instance { get; private set; }

        public bool IsConnected { get; private set; }
        public bool HasFailed { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string? CurrentRoomCode { get; private set; }

        public event Action<string>? Connected;
        public event Action<string>? Failed;

        private NetworkRunner? _runner;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public Task<bool> CreateRoom(string code) => ConnectShared(code, "create");

        public Task<bool> JoinRoom(string code) => ConnectShared(code, "join");

        private async Task<bool> ConnectShared(string code, string operation)
        {
            if (IsConnected)
            {
                Debug.LogWarning(
                    $"[PhotonBootstrap] {operation} ignored — already connected to " +
                    $"room {CurrentRoomCode}");
                return true;
            }

            try
            {
                _runner ??= gameObject.AddComponent<NetworkRunner>();
                StartGameArgs args = new()
                {
                    GameMode = GameMode.Shared,
                    SessionName = code,
                };

                StartGameResult result = await _runner.StartGame(args);

                if (result.Ok)
                {
                    IsConnected = true;
                    CurrentRoomCode = code;
                    Debug.Log(
                        $"[PhotonBootstrap] {operation} succeeded — connected to " +
                        $"room {code}");
                    Connected?.Invoke(code);
                    return true;
                }

                Fail($"{operation} failed: {result.ShutdownReason} — {result.ErrorMessage}");
                return false;
            }
            catch (Exception ex)
            {
                Fail($"{operation} exception: {ex.GetType().Name}: {ex.Message}");
                return false;
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
