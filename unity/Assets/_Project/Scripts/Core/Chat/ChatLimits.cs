#nullable enable

namespace Pose.Core.Chat
{
    /// <summary>
    /// The chat limits the client enforces locally, mirroring the server's
    /// (<c>functions/src/chat/model.ts</c>). The server is the authority — these
    /// exist so the composer can grey out a send that would only be refused after
    /// a round trip, not to replace the server check.
    /// </summary>
    public static class ChatLimits
    {
        /// <summary>Longest message the server will accept, after normalisation.</summary>
        public const int MaxMessageLength = 200;

        /// <summary>Seats at a table, and so the most members a room can hold.</summary>
        public const int MaxRoomMembers = 4;

        /// <summary>
        /// Messages kept in the panel's live view. The room holds more; this is
        /// what the scroll view renders, so an all-night session can't grow the
        /// UI without bound.
        /// </summary>
        public const int VisibleMessageCount = 100;
    }
}
