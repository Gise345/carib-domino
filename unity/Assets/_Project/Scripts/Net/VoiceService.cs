#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Functions;
using Pose.Core.Voice;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Client data layer for in-match voice (ADR 0024). Everything privileged goes
    /// through two Cloud Functions: <c>joinVoiceRoom</c> admits the caller and
    /// hands back the channel plus the non-secret Vivox settings, and
    /// <c>mintVivoxToken</c> signs one short-lived credential per Vivox action.
    ///
    /// No Vivox configuration is compiled into the client — it arrives from
    /// <see cref="JoinRoomAsync"/> at runtime, so switching environments is a
    /// config change rather than a store build.
    ///
    /// This type deliberately holds no Vivox SDK types: it is the transport half
    /// only, so it compiles and is reviewable independently of the SDK. The
    /// adapter that drives Vivox is <c>VivoxTokenProvider</c> / <c>VoiceController</c>.
    ///
    /// Static, like <see cref="ChatService"/>: it needs the initialised Firebase
    /// SDKs and nothing else.
    /// </summary>
    public static class VoiceService
    {
        /// <summary>The non-secret settings needed to initialise the Vivox SDK.</summary>
        public readonly struct VivoxSettings
        {
            /// <summary>Vivox API endpoint.</summary>
            public string Server { get; }

            /// <summary>Vivox domain the SIP URIs are built against.</summary>
            public string Domain { get; }

            /// <summary>Application-specific issuer.</summary>
            public string Issuer { get; }

            public VivoxSettings(string server, string domain, string issuer)
            {
                Server = server;
                Domain = domain;
                Issuer = issuer;
            }

            /// <summary>True once every value is present.</summary>
            public bool IsComplete =>
                !string.IsNullOrEmpty(Server)
                && !string.IsNullOrEmpty(Domain)
                && !string.IsNullOrEmpty(Issuer);
        }

        /// <summary>Outcome of asking the server for voice.</summary>
        public readonly struct JoinResult
        {
            /// <summary>What the server said.</summary>
            public VoiceJoinOutcome Outcome { get; }

            /// <summary>The Vivox channel to join. Empty unless admitted.</summary>
            public string ChannelName { get; }

            /// <summary>Whether this player may transmit.</summary>
            public bool CanSpeak { get; }

            /// <summary>How many players the room holds after the join.</summary>
            public int MemberCount { get; }

            /// <summary>Runtime Vivox settings; only populated when admitted.</summary>
            public VivoxSettings Vivox { get; }

            public JoinResult(
                VoiceJoinOutcome outcome,
                string channelName,
                bool canSpeak,
                int memberCount,
                VivoxSettings vivox)
            {
                Outcome = outcome;
                ChannelName = channelName;
                CanSpeak = canSpeak;
                MemberCount = memberCount;
                Vivox = vivox;
            }

            /// <summary>True when the server admitted this player.</summary>
            public bool IsOk => Outcome == VoiceJoinOutcome.Ok;

            /// <summary>A refusal, carrying no channel.</summary>
            /// <param name="outcome">Why voice was refused.</param>
            /// <returns>The refused result.</returns>
            public static JoinResult Refused(VoiceJoinOutcome outcome) =>
                new(outcome, string.Empty, false, 0, default);
        }

        private static FirebaseFunctions Functions => FirebaseFunctions.DefaultInstance;

        /// <summary>
        /// Asks the server to admit this player to a match's voice channel.
        ///
        /// The server claims the caller's own uid into the room roster — a host
        /// cannot enrol anyone else — which is what later lets
        /// <see cref="MintTokenAsync"/> authorise a join against proven membership.
        /// </summary>
        /// <param name="roomId">The Photon session name; one channel per series.</param>
        /// <param name="displayName">Name shown beside this player.</param>
        /// <param name="seat">Table seat index, or -1 when not yet seated.</param>
        /// <param name="matchId">Server-issued match id, for moderation context.</param>
        /// <param name="mode">Ruleset being played.</param>
        /// <param name="origin">How this table was reached; recorded, not a gate.</param>
        /// <returns>The join result; never null, refusals carry their reason.</returns>
        public static async Task<JoinResult> JoinRoomAsync(
            string roomId,
            string displayName,
            int seat,
            string? matchId,
            string? mode,
            VoiceRoomOrigin origin)
        {
            Dictionary<string, object> payload = new()
            {
                ["roomId"] = roomId,
                ["displayName"] = displayName,
                ["seat"] = seat,
                ["entry"] = origin == VoiceRoomOrigin.RandomMatchmaking ? "quickmatch" : "code",
            };
            if (!string.IsNullOrEmpty(matchId))
            {
                payload["matchId"] = matchId!;
            }
            if (!string.IsNullOrEmpty(mode))
            {
                payload["mode"] = mode!;
            }

            try
            {
                HttpsCallableResult call =
                    await Functions.GetHttpsCallable("joinVoiceRoom").CallAsync(payload);
                if (call.Data is not Dictionary<object, object> data)
                {
                    return JoinResult.Refused(VoiceJoinOutcome.Failed);
                }

                return new JoinResult(
                    VoiceJoinOutcome.Ok,
                    ReadString(data, "channelName", string.Empty),
                    ReadBool(data, "canSpeak", false),
                    (int)ReadLong(data, "memberCount", 0L),
                    ReadVivox(data));
            }
            catch (FunctionsException e)
            {
                VoiceJoinOutcome outcome = VoiceRefusal.Parse(
                    e.Message,
                    resourceExhausted: e.ErrorCode == FunctionsErrorCode.ResourceExhausted);

                // Voice being switched off is the expected state before the Vivox
                // setup is done, so it is not worth a console error.
                if (outcome != VoiceJoinOutcome.VoiceDisabled)
                {
                    Debug.LogWarning(
                        $"[VoiceService] joinVoiceRoom refused ({outcome}): {e.Message}");
                }
                return JoinResult.Refused(outcome);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceService] joinVoiceRoom failed: {e.Message}");
                return JoinResult.Refused(VoiceJoinOutcome.Failed);
            }
        }

        /// <summary>
        /// Mints one Vivox access token.
        ///
        /// Called once per Vivox action — the SDK asks for a fresh credential to
        /// log in, and again to join the channel — so every privileged step is
        /// re-authorised server-side. Tokens are single-use and expire in seconds,
        /// which is why there is nothing to cache or refresh here.
        ///
        /// Note what is NOT sent: the SDK offers its own idea of the caller's URI
        /// and the target channel, and neither is forwarded. The server builds both
        /// from the signed token and the room roster, because signing what a client
        /// asked for would mint a credential to join any channel as any user.
        /// </summary>
        /// <param name="action">Either <c>login</c> or <c>join</c>.</param>
        /// <param name="roomId">The room, required for a <c>join</c>.</param>
        /// <returns>The token, or null when refused.</returns>
        public static async Task<string?> MintTokenAsync(string action, string? roomId)
        {
            Dictionary<string, object> payload = new() { ["action"] = action };
            if (!string.IsNullOrEmpty(roomId))
            {
                payload["roomId"] = roomId!;
            }

            try
            {
                HttpsCallableResult call =
                    await Functions.GetHttpsCallable("mintVivoxToken").CallAsync(payload);
                if (call.Data is not Dictionary<object, object> data)
                {
                    return null;
                }

                string token = ReadString(data, "token", string.Empty);
                return string.IsNullOrEmpty(token) ? null : token;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceService] mintVivoxToken({action}) failed: {e.Message}");
                return null;
            }
        }

        private static VivoxSettings ReadVivox(IReadOnlyDictionary<object, object> data)
        {
            if (!data.TryGetValue("vivox", out object? raw)
                || raw is not Dictionary<object, object> vivox)
            {
                return default;
            }

            return new VivoxSettings(
                ReadString(vivox, "server", string.Empty),
                ReadString(vivox, "domain", string.Empty),
                ReadString(vivox, "issuer", string.Empty));
        }

        private static string ReadString(
            IReadOnlyDictionary<object, object> d, string key, string fallback) =>
            d.TryGetValue(key, out object? value) && value is string s ? s : fallback;

        private static long ReadLong(
            IReadOnlyDictionary<object, object> d, string key, long fallback) =>
            d.TryGetValue(key, out object? value) && value is long l ? l : fallback;

        private static bool ReadBool(
            IReadOnlyDictionary<object, object> d, string key, bool fallback) =>
            d.TryGetValue(key, out object? value) && value is bool b ? b : fallback;
    }
}
