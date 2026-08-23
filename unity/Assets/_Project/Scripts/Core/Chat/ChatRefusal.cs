#nullable enable
using System;

namespace Pose.Core.Chat
{
    /// <summary>
    /// Reads the refusal code the server prefixes onto a rejected send
    /// (`"muted: You are muted in chat."`).
    ///
    /// The prefix exists because Unity's <c>FunctionsException</c> exposes only an
    /// error code and a message — the callable's structured <c>details</c> payload
    /// never reaches the game client, so a client that needs to tell a mute from a
    /// rate limit has to read it off the message. Matching the human sentence
    /// instead would break the first time someone reworded it, hence a code.
    ///
    /// The writing half of this contract is <c>functions/src/chat/refusals.ts</c>.
    /// </summary>
    public static class ChatRefusal
    {
        /// <summary>Wire code for a guest being refused.</summary>
        public const string GuestCode = "guest-restricted";

        /// <summary>Wire code for an active moderator mute.</summary>
        public const string MutedCode = "muted";

        /// <summary>Wire code for exceeding the send allowance.</summary>
        public const string RateLimitedCode = "rate-limited";

        /// <summary>
        /// Classifies a server refusal.
        /// </summary>
        /// <param name="message">The exception message from the callable.</param>
        /// <param name="resourceExhausted">
        /// True when the SDK reported a resource-exhausted error code — the
        /// fallback that still catches a rate limit if the prefix is ever lost.
        /// </param>
        /// <returns>The outcome the panel should render.</returns>
        public static ChatSendOutcome Parse(string? message, bool resourceExhausted = false)
        {
            string code = CodeOf(message);

            if (string.Equals(code, GuestCode, StringComparison.Ordinal))
            {
                return ChatSendOutcome.GuestRestricted;
            }
            if (string.Equals(code, MutedCode, StringComparison.Ordinal))
            {
                return ChatSendOutcome.Muted;
            }
            if (string.Equals(code, RateLimitedCode, StringComparison.Ordinal) || resourceExhausted)
            {
                return ChatSendOutcome.RateLimited;
            }
            return ChatSendOutcome.Failed;
        }

        /// <summary>
        /// The code prefix of a refusal message, or empty when it carries none.
        /// </summary>
        /// <param name="message">The exception message from the callable.</param>
        /// <returns>The lowercase code, or an empty string.</returns>
        public static string CodeOf(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            int colon = message!.IndexOf(':');
            if (colon <= 0)
            {
                return string.Empty;
            }

            string candidate = message.Substring(0, colon).Trim();
            // A code is one short token: anything with a space is the start of a
            // sentence that happens to contain a colon, not a code.
            return candidate.Length <= 32 && candidate.IndexOf(' ') < 0
                ? candidate
                : string.Empty;
        }
    }
}
