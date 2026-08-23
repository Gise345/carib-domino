#nullable enable
using System.Text;

namespace Pose.Core.Chat
{
    /// <summary>
    /// What the composer does to the text a player types before it is worth
    /// sending: the same normalisation the server applies (control characters
    /// out, whitespace collapsed, trimmed), plus the local sendability check.
    ///
    /// Pure so it can be unit-tested; the server repeats every rule regardless.
    /// </summary>
    public static class ChatDraft
    {
        /// <summary>
        /// Normalises typed text the way <c>normalizeMessageText</c> does on the
        /// server, so the length the composer counts is the length the server
        /// measures.
        /// </summary>
        /// <param name="raw">Text straight from the input field.</param>
        /// <returns>The normalised text; may be empty.</returns>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            StringBuilder builder = new(raw!.Length);
            bool pendingSpace = false;

            foreach (char c in raw)
            {
                bool isSpace = char.IsWhiteSpace(c) || char.IsControl(c);
                if (isSpace)
                {
                    // Runs of whitespace — including the newline padding used to
                    // shout — collapse to a single space, and never lead.
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Whether the normalised draft is worth sending: non-empty and inside
        /// <see cref="ChatLimits.MaxMessageLength"/>.
        /// </summary>
        /// <param name="raw">Text straight from the input field.</param>
        /// <returns>True when the send button should be live.</returns>
        public static bool IsSendable(string? raw)
        {
            string normalized = Normalize(raw);
            return normalized.Length > 0 && normalized.Length <= ChatLimits.MaxMessageLength;
        }

        /// <summary>
        /// Characters still available, for the counter shown once a draft nears
        /// the limit. Negative once the draft is over.
        /// </summary>
        /// <param name="raw">Text straight from the input field.</param>
        /// <returns>Remaining characters against the server's limit.</returns>
        public static int Remaining(string? raw)
        {
            return ChatLimits.MaxMessageLength - Normalize(raw).Length;
        }
    }
}
