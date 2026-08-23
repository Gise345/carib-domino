#nullable enable

namespace Pose.Core.Voice
{
    /// <summary>
    /// Why the microphone is unavailable, which decides what the mic control says
    /// when tapped. <see cref="None"/> means the player may speak.
    ///
    /// Mirrors <see cref="Pose.Core.Chat.ChatLockReason"/> for the reasons the two
    /// features share, and adds the two only voice has.
    /// </summary>
    public enum VoiceLockReason
    {
        /// <summary>Voice is available.</summary>
        None = 0,

        /// <summary>Nobody is signed in yet, so there is no identity to speak under.</summary>
        SignedOut = 1,

        /// <summary>
        /// A guest. They hear the table but may not speak (ADR 0023 §3) — this is
        /// the state that offers the sign-up CTA, exactly as chat does.
        /// </summary>
        Guest = 2,

        /// <summary>A moderator has muted this player; a chat mute silences voice too.</summary>
        Muted = 3,

        /// <summary>Not in a room yet, so there is no channel to join.</summary>
        NoRoom = 4,

        /// <summary>
        /// Voice is off for this kind of table — at launch, random matchmaking
        /// (ADR 0024 §5). Widened by Remote Config, not by a build.
        /// </summary>
        NotAllowedInThisRoom = 5,

        /// <summary>
        /// The player refused the OS microphone prompt. The only reason here the
        /// player can fix themselves, so it is the only one that sends them to
        /// system settings.
        /// </summary>
        MicPermissionDenied = 6,
    }
}
