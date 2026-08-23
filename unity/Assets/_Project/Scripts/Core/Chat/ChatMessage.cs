#nullable enable
using System;

namespace Pose.Core.Chat
{
    /// <summary>
    /// One message as the panel renders it — the client-side mirror of a
    /// <c>/chatRooms/{roomId}/messages/{id}</c> document. Immutable: the room is
    /// server-written, so nothing here is ever edited locally.
    /// </summary>
    public sealed class ChatMessage
    {
        /// <summary>Firestore document id — what a report refers to.</summary>
        public string Id { get; }

        /// <summary>Sender's uid, stamped by the server from the signed token.</summary>
        public string SenderUid { get; }

        /// <summary>Sender's display name as it was when they joined the room.</summary>
        public string SenderName { get; }

        /// <summary>Sender's seat index, or -1 — drives the per-seat accent colour.</summary>
        public int Seat { get; }

        /// <summary>The delivered text: masked where the filter fired, empty if redacted.</summary>
        public string Text { get; }

        /// <summary>True when the profanity filter masked part of the message.</summary>
        public bool Filtered { get; }

        /// <summary>True once a moderator has removed the message.</summary>
        public bool Redacted { get; }

        /// <summary>Server timestamp; <see cref="DateTime.MinValue"/> until it lands.</summary>
        public DateTime CreatedAt { get; }

        public ChatMessage(
            string id,
            string senderUid,
            string senderName,
            int seat,
            string text,
            bool filtered,
            bool redacted,
            DateTime createdAt)
        {
            Id = id;
            SenderUid = senderUid;
            SenderName = senderName;
            Seat = seat;
            Text = text;
            Filtered = filtered;
            Redacted = redacted;
            CreatedAt = createdAt;
        }

        /// <summary>Whether this message was sent by the given player.</summary>
        /// <param name="uid">The local player's uid.</param>
        /// <returns>True when the local player wrote it — they can't report themselves.</returns>
        public bool IsFrom(string? uid) =>
            !string.IsNullOrEmpty(uid) && string.Equals(SenderUid, uid, StringComparison.Ordinal);
    }
}
