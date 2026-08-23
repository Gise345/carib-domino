#nullable enable

namespace Pose.Core.Chat
{
    /// <summary>
    /// Why the composer is locked, which decides what the panel says in place of
    /// the input row. <see cref="None"/> means the player may type.
    /// </summary>
    public enum ChatLockReason
    {
        /// <summary>Chat is available.</summary>
        None = 0,

        /// <summary>Nobody is signed in yet — chat is not reachable at all.</summary>
        SignedOut = 1,

        /// <summary>
        /// A guest. They may read the room, but sending needs an account
        /// (ADR 0023 §3) — this is the state that offers the sign-up CTA.
        /// </summary>
        Guest = 2,

        /// <summary>A moderator has muted this player for a period.</summary>
        Muted = 3,

        /// <summary>Not connected to a room yet, so there is nowhere to send.</summary>
        NoRoom = 4,
    }
}
