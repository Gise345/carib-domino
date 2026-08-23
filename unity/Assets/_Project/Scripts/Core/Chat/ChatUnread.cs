#nullable enable
using System.Collections.Generic;

namespace Pose.Core.Chat
{
    /// <summary>
    /// The unread count on the HUD's chat button.
    ///
    /// It exists so chat is worth closing. Without a count there is no way to
    /// know you missed anything, so the panel stays open over the board — which
    /// is exactly how it ends up covering the game.
    ///
    /// Pure: the view hands over what it last showed and gets a number back.
    /// </summary>
    public static class ChatUnread
    {
        /// <summary>Most the badge will say before it gives up counting.</summary>
        public const int Cap = 9;

        /// <summary>
        /// Counts messages the player has not seen: everything after the last
        /// one they had on screen, minus their own — a player's own message is
        /// never news to them.
        /// </summary>
        /// <param name="messages">The room's messages, oldest first.</param>
        /// <param name="lastSeenId">
        /// Id of the last message shown while the panel was open, or null when
        /// it has never been opened.
        /// </param>
        /// <param name="localUid">The local player's uid.</param>
        /// <returns>How many messages are unread.</returns>
        public static int Count(
            IReadOnlyList<ChatMessage> messages,
            string? lastSeenId,
            string? localUid)
        {
            int start = 0;
            if (!string.IsNullOrEmpty(lastSeenId))
            {
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    if (messages[i].Id == lastSeenId)
                    {
                        start = i + 1;
                        break;
                    }
                }
            }

            int unread = 0;
            for (int i = start; i < messages.Count; i++)
            {
                if (!messages[i].IsFrom(localUid))
                {
                    unread++;
                }
            }
            return unread;
        }

        /// <summary>
        /// The badge text for a count: empty when there is nothing to say, and
        /// capped so a long absence doesn't produce a badge wider than the
        /// button it sits on.
        /// </summary>
        /// <param name="unread">The count from <see cref="Count"/>.</param>
        /// <returns>"", "3", or "9+".</returns>
        public static string Badge(int unread)
        {
            if (unread <= 0)
            {
                return string.Empty;
            }
            return unread > Cap ? $"{Cap}+" : unread.ToString();
        }
    }
}
