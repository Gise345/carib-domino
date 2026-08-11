#nullable enable
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// The Fusion runner this bootstrap owns. <c>null</c> until the first
        /// <see cref="CreateRoom"/> or <see cref="JoinRoom"/> call. Exposed so
        /// <see cref="OnlineMatchController"/> can spawn networked objects on
        /// the same runner.
        /// </summary>
        public NetworkRunner? Runner => _runner;

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

        /// <summary>
        /// Creates a room capped at <paramref name="playerCount"/> seats so no
        /// more than the host's chosen number of players can join.
        /// </summary>
        public Task<bool> CreateRoom(string code, int playerCount) =>
            ConnectShared(code, "create", playerCount);

        /// <summary>Joins an existing room; the room's capacity was set by the host.</summary>
        public Task<bool> JoinRoom(string code) => ConnectShared(code, "join", playerCount: 0);

        /// <summary>
        /// Random matchmaking: joins an open table of the given ruleset (and, for
        /// Cut-Throat, size) or, if none exists, creates one. No room code — Photon
        /// groups players by the published <see cref="Pose.Core.Matchmaking"/>
        /// properties (mode + size), so different modes/sizes never cross-match,
        /// and the default <c>MatchmakingMode.FillRoom</c> fills partial tables
        /// first. The session creator becomes the shared-mode master client (the
        /// invisible authority) via the same check the code-room path uses.
        /// Partner is always a 4-seat 2-v-2 table (size ignored).
        /// </summary>
        /// <param name="mode">Ruleset to matchmake for.</param>
        /// <param name="size">Table size, 2–4 (Cut-Throat only).</param>
        public Task<bool> QuickMatch(Pose.Core.GameMode mode, int size)
        {
            bool partner = mode == Pose.Core.GameMode.Partner;
            int seats = partner ? Pose.Core.Matchmaking.PartnerSize : size;

            Dictionary<string, SessionProperty> props = new();
            foreach (KeyValuePair<string, string> kv in Pose.Core.Matchmaking.Properties(mode, size))
            {
                props[kv.Key] = kv.Value; // implicit string -> SessionProperty
            }

            // SessionName null → Photon assigns one and matchmakes on the props.
            return ConnectShared(sessionName: null, partner ? "partner-online" : "cutthroat-online", seats, props);
        }

        private async Task<bool> ConnectShared(
            string? sessionName,
            string operation,
            int playerCount,
            Dictionary<string, SessionProperty>? sessionProperties = null)
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
                    // Null for random matchmaking — Photon assigns the name and
                    // matches on SessionProperties; a code for private rooms.
                    SessionName = sessionName,
                    // Only the creator sets capacity; code-room joiners pass 0
                    // (unset) and inherit the host's. Matchmaking players all pass
                    // the same size, so whoever creates the session sets it.
                    PlayerCount = playerCount > 0 ? playerCount : null,
                    SessionProperties = sessionProperties,
                };

                StartGameResult result = await _runner.StartGame(args);

                if (result.Ok)
                {
                    IsConnected = true;
                    // Matchmaking has no caller-supplied code — read the name
                    // Photon assigned so logs/UI have something to show.
                    CurrentRoomCode = sessionName ?? _runner.SessionInfo?.Name;
                    Debug.Log(
                        $"[PhotonBootstrap] {operation} succeeded — connected to " +
                        $"session {CurrentRoomCode}");
                    Connected?.Invoke(CurrentRoomCode ?? string.Empty);
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
