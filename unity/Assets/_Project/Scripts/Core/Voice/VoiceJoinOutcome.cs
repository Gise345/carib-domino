#nullable enable

namespace Pose.Core.Voice
{
    /// <summary>
    /// What happened when the client asked the server for voice. Distinct from
    /// <see cref="VoiceLockReason"/>: that is what the client already knows before
    /// asking, this is what the server said back. They can disagree — a client
    /// showing an unlocked mic against a stale entitlement still gets refused —
    /// and when they do, the server wins.
    /// </summary>
    public enum VoiceJoinOutcome
    {
        /// <summary>Admitted; a channel and a token are available.</summary>
        Ok = 0,

        /// <summary>A guest. They neither speak nor listen (ADR 0024 §3).</summary>
        GuestRestricted = 1,

        /// <summary>A moderator mute is in force.</summary>
        Muted = 2,

        /// <summary>The caller is not a member of that room.</summary>
        NotInRoom = 3,

        /// <summary>
        /// Voice is not provisioned or not open here. Expected, not an error —
        /// it is what the server says before the Vivox setup is done.
        /// </summary>
        VoiceDisabled = 4,

        /// <summary>Too many token requests in the window.</summary>
        RateLimited = 5,

        /// <summary>Anything else — offline, banned, a server fault.</summary>
        Failed = 6,
    }
}
