#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Pose.Core.Voice;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

namespace Pose.Net.Voice
{
    /// <summary>
    /// Drives the Vivox session for a match (ADR 0024). The single implementation
    /// of <see cref="IVoiceSession"/>, and the only place in the project that
    /// calls the Vivox SDK.
    ///
    /// It self-registers: nothing in <c>Assembly-CSharp</c> constructs it, because
    /// nothing there can see this assembly. It creates itself before the first
    /// scene loads and publishes itself through <see cref="VoiceRuntime"/>. If this
    /// assembly is not built — the Vivox package missing, an SDK API moved — that
    /// registration simply never happens and the game runs without voice.
    /// </summary>
    public sealed class VoiceController : MonoBehaviour, IVoiceSession
    {
        /// <summary>Seats at a table, and so the size of the local mute set.</summary>
        private const int SeatCount = 4;

        /// <summary>
        /// Voice-activity tuning, set once before joining. Vivox's defaults assume
        /// a quieter room than a phone in a front room with a TV on: the trailing
        /// window is shortened so the mic closes promptly after a sentence instead
        /// of broadcasting the room, and the floor is raised for the same reason.
        /// Sensitivity is left at the default — note it is INVERTED, higher means
        /// less sensitive.
        /// </summary>
        private const int VadHangoverMs = 1200;
        private const int VadNoiseFloor = 900;
        private const int VadSensitivity = 43;

        private static bool _servicesInitialized;

        private readonly VivoxTokenProvider _tokenProvider = new();
        private readonly bool[] _seatMuted = new bool[SeatCount];
        private readonly Dictionary<string, int> _uidToSeat = new(StringComparer.Ordinal);

        /// <summary>Live participants by uid, with the handler used to unsubscribe.</summary>
        private readonly Dictionary<string, (VivoxParticipant Participant, Action Handler)> _tracked =
            new(StringComparer.Ordinal);

        private string? _channelName;
        private string? _roomId;
        private bool _appPaused;

        /// <inheritdoc/>
        public bool IsConnected { get; private set; }

        /// <inheritdoc/>
        public bool CanSpeak { get; private set; }

        /// <inheritdoc/>
        public VoiceJoinOutcome LastOutcome { get; private set; } = VoiceJoinOutcome.VoiceDisabled;

        /// <inheritdoc/>
        public bool IsSelfMuted { get; private set; }

        /// <inheritdoc/>
        public MicPermissionState MicPermission => Voice.MicPermission.Current;

        /// <inheritdoc/>
        public event Action? StateChanged;

        /// <inheritdoc/>
        public event Action<int, bool>? SeatSpeakingChanged;

        /// <summary>
        /// Creates the controller and publishes it, before any scene loads.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            GameObject host = new("VoiceController");
            DontDestroyOnLoad(host);
            VoiceRuntime.Register(host.AddComponent<VoiceController>());
        }

        /// <inheritdoc/>
        public void SetSeatMap(IReadOnlyDictionary<string, int> uidToSeat)
        {
            _uidToSeat.Clear();
            foreach (KeyValuePair<string, int> entry in uidToSeat)
            {
                _uidToSeat[entry.Key] = entry.Value;
            }
        }

        /// <inheritdoc/>
        public async Task<VoiceJoinOutcome> BeginAsync(
            string roomId,
            string displayName,
            int seat,
            string? matchId,
            string? mode,
            VoiceRoomOrigin origin)
        {
            if (IsConnected)
            {
                await EndAsync();
            }

            // The server decides first. Nothing Vivox-shaped happens for a guest,
            // a banned or muted player, or a table voice is not open on — which is
            // also what keeps them off the concurrent-user bill.
            VoiceService.JoinResult join =
                await VoiceService.JoinRoomAsync(roomId, displayName, seat, matchId, mode, origin);

            LastOutcome = join.Outcome;
            if (!join.IsOk || !join.Vivox.IsComplete)
            {
                Publish();
                return LastOutcome;
            }

            // Asked here, at the first real use, rather than at launch: a prompt
            // that arrives before the player knows why is reliably refused.
            MicPermissionState permission = await Voice.MicPermission.RequestAsync();

            string uid = FirebaseAuth.DefaultInstance?.CurrentUser?.UserId ?? string.Empty;
            if (uid.Length == 0)
            {
                LastOutcome = VoiceJoinOutcome.Failed;
                Publish();
                return LastOutcome;
            }

            try
            {
                await EnsureServicesAsync(join.Vivox);

                _roomId = roomId;
                _channelName = join.ChannelName;
                _tokenProvider.SetRoom(roomId);

                if (!VivoxService.Instance.IsLoggedIn)
                {
                    // PlayerId is the Firebase uid, read from the same auth state
                    // the callable's token is stamped from, so it always matches
                    // the `f` claim the server signs.
                    await VivoxService.Instance.LoginAsync(
                        new LoginOptions { PlayerId = uid, DisplayName = displayName });
                }

                // Must precede the join: Vivox ignores noise-floor changes on a
                // channel that has already been entered.
                await VivoxService.Instance.SetVoiceActivityDetectionPropertiesAsync(
                    VadHangoverMs, VadNoiseFloor, VadSensitivity);
                VivoxService.Instance.EnableAcousticEchoCancellation();

                VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;

                // AudioOnly, never TextAndAudio: Vivox's own text channel would be
                // a second, entirely unmoderated conversation running alongside the
                // one ADR 0023 built the whole moderation spine around.
                await VivoxService.Instance.JoinGroupChannelAsync(
                    join.ChannelName, ChatCapability.AudioOnly);

                IsConnected = true;
                CanSpeak = join.CanSpeak && permission == MicPermissionState.Granted;
                await ApplyTransmissionAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceController] could not join voice: {e.Message}");
                LastOutcome = VoiceJoinOutcome.Failed;
                await EndAsync();
                return LastOutcome;
            }

            Publish();
            return LastOutcome;
        }

        /// <inheritdoc/>
        public async Task EndAsync()
        {
            UntrackAll();

            if (VivoxService.Instance != null)
            {
                VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;

                try
                {
                    if (IsConnected)
                    {
                        await VivoxService.Instance.LeaveAllChannelsAsync();
                    }
                }
                catch (Exception e)
                {
                    // Leaving is best-effort: the session is going away regardless,
                    // and a throw here would strand the local state as "connected".
                    Debug.LogWarning($"[VoiceController] leave failed: {e.Message}");
                }
            }

            IsConnected = false;
            CanSpeak = false;
            IsSelfMuted = false;
            _channelName = null;
            _roomId = null;
            _tokenProvider.SetRoom(null);
            Array.Clear(_seatMuted, 0, _seatMuted.Length);
            Publish();
        }

        /// <inheritdoc/>
        public void SetSelfMuted(bool muted)
        {
            if (IsSelfMuted == muted)
            {
                return;
            }

            IsSelfMuted = muted;
            _ = ApplyTransmissionAsync();
            Publish();
        }

        /// <inheritdoc/>
        public void SetSeatMuted(int seat, bool muted)
        {
            if (seat < 0 || seat >= SeatCount || _seatMuted[seat] == muted)
            {
                return;
            }

            _seatMuted[seat] = muted;

            foreach (KeyValuePair<string, (VivoxParticipant Participant, Action Handler)> entry in _tracked)
            {
                if (SeatOf(entry.Key) != seat)
                {
                    continue;
                }

                // Listener-side only: everyone else at the table still hears them.
                if (muted)
                {
                    entry.Value.Participant.MutePlayerLocally();
                }
                else
                {
                    entry.Value.Participant.UnmutePlayerLocally();
                }
            }

            Publish();
        }

        /// <inheritdoc/>
        public bool IsSeatMuted(int seat) =>
            seat >= 0 && seat < SeatCount && _seatMuted[seat];

        /// <summary>
        /// Releases the microphone while the game is in the background, and takes
        /// it back on return. Without this the OS recording indicator sits lit
        /// while the player is in another app, which reads as the game listening
        /// when it should not be.
        /// </summary>
        /// <param name="paused">True when the app has gone to the background.</param>
        private void OnApplicationPause(bool paused)
        {
            _appPaused = paused;
            if (IsConnected)
            {
                _ = ApplyTransmissionAsync();
            }
        }

        /// <summary>
        /// Brings up Unity Gaming Services and Vivox once per process.
        /// </summary>
        /// <param name="settings">Runtime credentials from <c>joinVoiceRoom</c>.</param>
        /// <returns>A task that completes once Vivox is ready.</returns>
        private async Task EnsureServicesAsync(VoiceService.VivoxSettings settings)
        {
            if (_servicesInitialized)
            {
                return;
            }

            // Registered before initialisation, and before any login: the SDK
            // resolves the provider late, so a missing one surfaces as an
            // unhelpful failure at login rather than here.
            VivoxService.Instance.SetTokenProvider(_tokenProvider);

            // No token key is passed — that is the whole point. The signing key
            // never leaves Secret Manager; the client only ever receives tokens.
            InitializationOptions options = new InitializationOptions()
                .SetVivoxCredentials(settings.Server, settings.Domain, settings.Issuer);

            await UnityServices.InitializeAsync(options);
            await VivoxService.Instance.InitializeAsync();
            _servicesInitialized = true;
        }

        /// <summary>
        /// Applies the current transmit decision to Vivox.
        /// </summary>
        /// <returns>A task that completes once the mode is set.</returns>
        private async Task ApplyTransmissionAsync()
        {
            if (!IsConnected || _channelName is null)
            {
                return;
            }

            bool transmitting = CanSpeak && !IsSelfMuted && !_appPaused;

            // Self-mute stops transmission but KEEPS the device, so un-muting is
            // instant. Backgrounding releases it outright — a different thing,
            // deliberately handled differently.
            if (transmitting)
            {
                VivoxService.Instance.UnmuteInputDevice();
            }
            else
            {
                VivoxService.Instance.MuteInputDevice();
            }

            try
            {
                await VivoxService.Instance.SetChannelTransmissionModeAsync(
                    transmitting ? TransmissionMode.Single : TransmissionMode.None, _channelName);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceController] transmission mode failed: {e.Message}");
            }
        }

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant.IsSelf || _tracked.ContainsKey(participant.PlayerId))
            {
                return;
            }

            void Handler() => RaiseSpeaking(participant);
            participant.ParticipantSpeechDetected += Handler;
            _tracked[participant.PlayerId] = (participant, Handler);

            // A player who re-joins mid-series must come back still muted.
            int seat = SeatOf(participant.PlayerId);
            if (seat >= 0 && _seatMuted[seat])
            {
                participant.MutePlayerLocally();
            }
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            if (!_tracked.TryGetValue(participant.PlayerId, out var entry))
            {
                return;
            }

            entry.Participant.ParticipantSpeechDetected -= entry.Handler;
            _tracked.Remove(participant.PlayerId);

            int seat = SeatOf(participant.PlayerId);
            if (seat >= 0)
            {
                SeatSpeakingChanged?.Invoke(seat, false);
            }
        }

        private void UntrackAll()
        {
            foreach (KeyValuePair<string, (VivoxParticipant Participant, Action Handler)> entry in _tracked)
            {
                entry.Value.Participant.ParticipantSpeechDetected -= entry.Value.Handler;
            }
            _tracked.Clear();
        }

        private void RaiseSpeaking(VivoxParticipant participant)
        {
            int seat = SeatOf(participant.PlayerId);
            if (seat >= 0)
            {
                SeatSpeakingChanged?.Invoke(seat, participant.SpeechDetected);
            }
        }

        private int SeatOf(string uid) =>
            _uidToSeat.TryGetValue(uid, out int seat) ? seat : -1;

        private void Publish() => StateChanged?.Invoke();
    }
}
