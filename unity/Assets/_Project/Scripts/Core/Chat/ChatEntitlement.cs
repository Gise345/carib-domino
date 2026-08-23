#nullable enable

namespace Pose.Core.Chat
{
    /// <summary>
    /// What the local player may do in chat and voice right now, derived from
    /// session state. Pure and immutable: the panel asks this rather than
    /// scattering `IsGuest` checks through the view, and the same rule decides
    /// the microphone, so chat and voice can never disagree (ADR 0023 §3).
    ///
    /// This is presentation only. The server re-checks every rule — an unlocked
    /// composer on a tampered client still gets its message refused.
    /// </summary>
    public readonly struct ChatEntitlement
    {
        /// <summary>Whether the conversation may be shown.</summary>
        public bool CanRead { get; }

        /// <summary>Whether the composer accepts input.</summary>
        public bool CanSend { get; }

        /// <summary>Whether the microphone control is live (the later voice feature).</summary>
        public bool CanUseVoice { get; }

        /// <summary>Why sending is unavailable, or <see cref="ChatLockReason.None"/>.</summary>
        public ChatLockReason LockReason { get; }

        private ChatEntitlement(bool canRead, bool canSend, bool canUseVoice, ChatLockReason lockReason)
        {
            CanRead = canRead;
            CanSend = canSend;
            CanUseVoice = canUseVoice;
            LockReason = lockReason;
        }

        /// <summary>
        /// Works out what this session may do.
        /// </summary>
        /// <param name="isSignedIn">True once Firebase auth has a user.</param>
        /// <param name="isGuest">True for an anonymous (guest) session.</param>
        /// <param name="isMuted">True while a moderator mute is in force.</param>
        /// <param name="hasRoom">True once the client has joined a chat room.</param>
        /// <returns>The entitlement the panel should render.</returns>
        public static ChatEntitlement For(bool isSignedIn, bool isGuest, bool isMuted, bool hasRoom)
        {
            if (!isSignedIn)
            {
                return new ChatEntitlement(false, false, false, ChatLockReason.SignedOut);
            }

            // A guest reads the room but never sends and never speaks: an
            // anonymous identity costs one tap to replace, so it can't be
            // moderated. Read stays open so they can follow the table — and see
            // what they're being offered an account for.
            if (isGuest)
            {
                return new ChatEntitlement(true, false, false, ChatLockReason.Guest);
            }

            if (isMuted)
            {
                return new ChatEntitlement(true, false, false, ChatLockReason.Muted);
            }

            if (!hasRoom)
            {
                return new ChatEntitlement(true, false, false, ChatLockReason.NoRoom);
            }

            return new ChatEntitlement(true, true, true, ChatLockReason.None);
        }
    }
}
