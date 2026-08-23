#nullable enable

namespace Pose.Core.Chat
{
    /// <summary>
    /// How a table names itself in the chat header.
    ///
    /// The room id is a Photon session name, which is one of two things: a
    /// six-character code someone typed to join a friend's table, or an id
    /// Photon assigned when matchmaking put strangers together. Printing the
    /// second one is what put <c>Table dl3010f3-ec3a-4f81…</c> in front of
    /// players — meaningless to them, and no help to anyone.
    /// </summary>
    public static class ChatRoomLabel
    {
        /// <summary>Length of a code from <c>RoomCodeGenerator</c>.</summary>
        private const int CodeLength = 6;

        /// <summary>
        /// The generator's alphabet: no I, O, 0 or 1, so a code can be read
        /// aloud across a room without being misheard.
        /// </summary>
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>
        /// Whether a room id is a code a player could type to join — and so
        /// worth showing them. A matchmade session id is not.
        /// </summary>
        /// <param name="roomId">The Photon session name.</param>
        /// <returns>True when the id is a shareable table code.</returns>
        public static bool IsJoinableCode(string? roomId)
        {
            if (roomId == null || roomId.Length != CodeLength)
            {
                return false;
            }

            foreach (char c in roomId)
            {
                if (CodeAlphabet.IndexOf(char.ToUpperInvariant(c)) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The code to show beside the table's name, or null when there is
        /// nothing worth showing.
        /// </summary>
        /// <param name="roomId">The Photon session name.</param>
        /// <returns>The uppercase code, or null.</returns>
        public static string? DisplayCode(string? roomId) =>
            IsJoinableCode(roomId) ? roomId!.ToUpperInvariant() : null;
    }
}
