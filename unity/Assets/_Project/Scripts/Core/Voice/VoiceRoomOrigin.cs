#nullable enable

namespace Pose.Core.Voice
{
    /// <summary>
    /// How the player got into this table, which is what decides whether they are
    /// sitting with people they know. Voice scope turns on this distinction rather
    /// than on the ruleset alone (ADR 0024 §5).
    /// </summary>
    public enum VoiceRoomOrigin
    {
        /// <summary>Not in an online room — local or bot play.</summary>
        None = 0,

        /// <summary>
        /// Joined by sharing a room code, so the table is friends. The low-risk
        /// case, and the one voice launches on.
        /// </summary>
        PrivateCode = 1,

        /// <summary>
        /// Grouped by Photon session properties, so the table is strangers. The
        /// high-risk case: voice leaves no transcript to review after the fact.
        /// </summary>
        RandomMatchmaking = 2,
    }
}
