#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using Firebase.Functions;
using Pose.Core.Chat;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Client data layer for in-match chat (ADR 0023). Sends through the
    /// <c>joinChatRoom</c> / <c>sendChatMessage</c> / <c>reportChatMessage</c>
    /// Cloud Functions and receives through a Firestore <b>snapshot listener</b>
    /// on <c>/chatRooms/{roomId}/messages</c>.
    ///
    /// The split is deliberate: the write path is where every abuse gate lives, so
    /// it runs on the server; the read path only needs to be fast, and a listener
    /// delivers without polling. The client cannot write to a chat room at all —
    /// Firestore rules deny it — so nothing here is trusted.
    ///
    /// Static, like <see cref="SocialService"/>: it needs the initialised Firebase
    /// SDKs and nothing else. <see cref="Subscribe"/> returns an
    /// <see cref="IDisposable"/>; dispose it when the panel or match goes away.
    /// </summary>
    public static class ChatService
    {
        /// <summary>Outcome of joining a room.</summary>
        public readonly struct JoinResult
        {
            /// <summary>The room the client is now listening to.</summary>
            public string RoomId { get; }

            /// <summary>How many players the room holds after the join.</summary>
            public int MemberCount { get; }

            /// <summary>False for a guest — read allowed, send refused (ADR 0023 §3).</summary>
            public bool CanSend { get; }

            public JoinResult(string roomId, int memberCount, bool canSend)
            {
                RoomId = roomId;
                MemberCount = memberCount;
                CanSend = canSend;
            }
        }

        /// <summary>Why a send failed, in a form the panel can show.</summary>
        public enum SendOutcome
        {
            /// <summary>Delivered.</summary>
            Ok = 0,

            /// <summary>The account is a guest — offer the sign-up CTA.</summary>
            GuestRestricted = 1,

            /// <summary>A moderator mute is in force.</summary>
            Muted = 2,

            /// <summary>Sending too fast; try again shortly.</summary>
            RateLimited = 3,

            /// <summary>Anything else — network, validation, or a server refusal.</summary>
            Failed = 4,
        }

        /// <summary>Result of one send attempt.</summary>
        public readonly struct SendResult
        {
            public SendOutcome Outcome { get; }

            /// <summary>True when the server masked part of the delivered text.</summary>
            public bool Filtered { get; }

            /// <summary>Server-supplied detail, already localised where it can be.</summary>
            public string Message { get; }

            public SendResult(SendOutcome outcome, bool filtered, string message)
            {
                Outcome = outcome;
                Filtered = filtered;
                Message = message;
            }

            public bool IsOk => Outcome == SendOutcome.Ok;
        }

        private static FirebaseFunctions Functions => FirebaseFunctions.DefaultInstance;

        /// <summary>
        /// Joins (creating on first call) the chat room for a match. The server
        /// adds the CALLER'S OWN uid to the roster — a host cannot enrol anyone —
        /// which is what makes the Firestore read rule trustworthy.
        /// </summary>
        /// <param name="roomId">The Photon session name; one room per series.</param>
        /// <param name="displayName">Name shown beside this player's messages.</param>
        /// <param name="seat">Table seat index, or -1 when not yet seated.</param>
        /// <param name="matchId">Server-issued match id, for moderation context.</param>
        /// <param name="mode">Ruleset being played.</param>
        /// <returns>The join result, or null when the call failed.</returns>
        public static async Task<JoinResult?> JoinRoomAsync(
            string roomId,
            string displayName,
            int seat,
            string? matchId,
            string? mode)
        {
            Dictionary<string, object> payload = new()
            {
                ["roomId"] = roomId,
                ["displayName"] = displayName,
                ["seat"] = seat,
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
                    await Functions.GetHttpsCallable("joinChatRoom").CallAsync(payload);
                if (call.Data is not Dictionary<object, object> data)
                {
                    return null;
                }

                return new JoinResult(
                    ReadString(data, "roomId", roomId),
                    (int)ReadLong(data, "memberCount", 0L),
                    ReadBool(data, "canSend", false));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChatService] joinChatRoom failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sends a message. Every rule — guest, ban, mute, membership, rate limit,
        /// length, profanity — is applied server-side; the local checks in
        /// <see cref="ChatDraft"/> only spare the round trip.
        /// </summary>
        /// <param name="roomId">The room to post into.</param>
        /// <param name="text">The player's typed text.</param>
        /// <returns>What happened, in a form the panel can render.</returns>
        public static async Task<SendResult> SendAsync(string roomId, string text)
        {
            Dictionary<string, object> payload = new()
            {
                ["roomId"] = roomId,
                ["text"] = text,
            };

            try
            {
                HttpsCallableResult call =
                    await Functions.GetHttpsCallable("sendChatMessage").CallAsync(payload);
                bool filtered = call.Data is Dictionary<object, object> data
                                && ReadBool(data, "filtered", false);
                return new SendResult(SendOutcome.Ok, filtered, string.Empty);
            }
            catch (FunctionsException e)
            {
                return new SendResult(ClassifyFailure(e), false, e.Message);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChatService] sendChatMessage failed: {e.Message}");
                return new SendResult(SendOutcome.Failed, false, e.Message);
            }
        }

        /// <summary>
        /// Flags a message for moderation. The server freezes the room's
        /// transcript, roster and match ids into an immutable report, so the
        /// evidence survives even if the messages are later swept.
        /// </summary>
        /// <param name="roomId">The room the message is in.</param>
        /// <param name="messageId">The message being reported.</param>
        /// <param name="reason">Why it is being reported.</param>
        /// <param name="note">Optional free-text detail from the reporter.</param>
        /// <returns>True when the report was filed (or already had been).</returns>
        public static async Task<bool> ReportAsync(
            string roomId,
            string messageId,
            ChatReportReason reason,
            string note)
        {
            Dictionary<string, object> payload = new()
            {
                ["roomId"] = roomId,
                ["messageId"] = messageId,
                ["reason"] = reason.ToWire(),
                ["note"] = note ?? string.Empty,
            };

            try
            {
                await Functions.GetHttpsCallable("reportChatMessage").CallAsync(payload);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChatService] reportChatMessage failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Listens to a room's messages, oldest first, and raises
        /// <paramref name="onMessages"/> on every change. Dispose the returned
        /// handle to stop listening (leaving a listener alive keeps a Firestore
        /// stream open and bills reads for a room nobody is watching).
        /// </summary>
        /// <param name="roomId">The room to watch.</param>
        /// <param name="onMessages">Called with the full ordered message list.</param>
        /// <returns>A handle that detaches the listener when disposed.</returns>
        public static IDisposable Subscribe(string roomId, Action<IReadOnlyList<ChatMessage>> onMessages)
        {
            Query query = FirebaseFirestore.DefaultInstance
                .Collection("chatRooms")
                .Document(roomId)
                .Collection("messages")
                .OrderByDescending("createdAt")
                .Limit(ChatLimits.VisibleMessageCount);

            ListenerRegistration registration = query.Listen(snapshot =>
            {
                List<ChatMessage> messages = new(snapshot.Count);
                foreach (DocumentSnapshot doc in snapshot.Documents)
                {
                    messages.Add(ToMessage(doc));
                }
                // The query takes the NEWEST hundred; the panel reads oldest-first.
                messages.Reverse();
                onMessages(messages);
            });

            return new Subscription(registration);
        }

        private static ChatMessage ToMessage(DocumentSnapshot doc)
        {
            Dictionary<string, object> d = doc.ToDictionary();
            DateTime createdAt = d.TryGetValue("createdAt", out object? raw) && raw is Timestamp ts
                ? ts.ToDateTime()
                : DateTime.MinValue;

            return new ChatMessage(
                doc.Id,
                Field(d, "senderUid"),
                Field(d, "senderName"),
                d.TryGetValue("seat", out object? seat) && seat is long s ? (int)s : -1,
                Field(d, "text"),
                Flag(d, "filtered"),
                Flag(d, "redacted"),
                createdAt);
        }

        private static string Field(IReadOnlyDictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object? value) && value is string s ? s : string.Empty;

        private static bool Flag(IReadOnlyDictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object? value) && value is bool b && b;

        /// <summary>
        /// Maps the server's refusal onto something the panel can act on. The
        /// codes come from the callables' error details, not from message text.
        /// </summary>
        private static SendOutcome ClassifyFailure(FunctionsException e)
        {
            string code = string.Empty;
            if (e.Data is Dictionary<object, object> details
                && details.TryGetValue("code", out object? value)
                && value is string s)
            {
                code = s;
            }

            return code switch
            {
                "guest-restricted" => SendOutcome.GuestRestricted,
                "muted" => SendOutcome.Muted,
                "rate-limited" => SendOutcome.RateLimited,
                _ => SendOutcome.Failed,
            };
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

        /// <summary>Detaches a Firestore listener when disposed.</summary>
        private sealed class Subscription : IDisposable
        {
            private ListenerRegistration? _registration;

            public Subscription(ListenerRegistration registration) => _registration = registration;

            public void Dispose()
            {
                _registration?.Stop();
                _registration = null;
            }
        }
    }
}
