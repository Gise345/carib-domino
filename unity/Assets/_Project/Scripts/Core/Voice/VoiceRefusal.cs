#nullable enable
using System;
using Pose.Core.Chat;

namespace Pose.Core.Voice
{
    /// <summary>
    /// Reads the refusal code the server prefixes onto a rejected voice call
    /// (`"voice-disabled: Voice is not available yet."`).
    ///
    /// Shares the prefix contract with chat rather than inventing a second one —
    /// <see cref="ChatRefusal.CodeOf"/> does the parsing — because the reason the
    /// contract exists is a property of Unity's <c>FunctionsException</c>, not of
    /// chat: it carries only an error code and a message, so a callable's
    /// structured <c>details</c> payload never reaches the game.
    ///
    /// The writing half is <c>functions/src/voice/model.ts</c> and
    /// <c>functions/src/chat/refusals.ts</c>.
    /// </summary>
    public static class VoiceRefusal
    {
        /// <summary>Wire code for voice being unavailable or not open here.</summary>
        public const string VoiceDisabledCode = "voice-disabled";

        /// <summary>Wire code for the caller not being a member of the room.</summary>
        public const string NotInRoomCode = "not-in-room";

        /// <summary>
        /// Classifies a server refusal.
        /// </summary>
        /// <param name="message">The exception message from the callable.</param>
        /// <param name="resourceExhausted">
        /// True when the SDK reported resource-exhausted — the fallback that still
        /// catches a rate limit if the prefix is ever lost.
        /// </param>
        /// <returns>The outcome the mic control should render.</returns>
        public static VoiceJoinOutcome Parse(string? message, bool resourceExhausted = false)
        {
            string code = ChatRefusal.CodeOf(message);

            if (string.Equals(code, ChatRefusal.GuestCode, StringComparison.Ordinal))
            {
                return VoiceJoinOutcome.GuestRestricted;
            }
            if (string.Equals(code, ChatRefusal.MutedCode, StringComparison.Ordinal))
            {
                return VoiceJoinOutcome.Muted;
            }
            if (string.Equals(code, VoiceDisabledCode, StringComparison.Ordinal))
            {
                return VoiceJoinOutcome.VoiceDisabled;
            }
            if (string.Equals(code, NotInRoomCode, StringComparison.Ordinal))
            {
                return VoiceJoinOutcome.NotInRoom;
            }
            if (string.Equals(code, ChatRefusal.RateLimitedCode, StringComparison.Ordinal)
                || resourceExhausted)
            {
                return VoiceJoinOutcome.RateLimited;
            }
            return VoiceJoinOutcome.Failed;
        }

        /// <summary>
        /// Which lock a refusal should leave the mic control showing, so a server
        /// refusal and a locally-derived lock render the same way.
        /// </summary>
        /// <param name="outcome">The server's answer.</param>
        /// <returns>The matching lock reason.</returns>
        public static VoiceLockReason ToLockReason(VoiceJoinOutcome outcome) => outcome switch
        {
            VoiceJoinOutcome.Ok => VoiceLockReason.None,
            VoiceJoinOutcome.GuestRestricted => VoiceLockReason.Guest,
            VoiceJoinOutcome.Muted => VoiceLockReason.Muted,
            VoiceJoinOutcome.NotInRoom => VoiceLockReason.NoRoom,
            VoiceJoinOutcome.VoiceDisabled => VoiceLockReason.NotAllowedInThisRoom,
            // A rate limit is transient and self-clearing, so it reads as "not
            // here yet" rather than as an accusation.
            VoiceJoinOutcome.RateLimited => VoiceLockReason.NoRoom,
            _ => VoiceLockReason.NoRoom,
        };
    }
}
