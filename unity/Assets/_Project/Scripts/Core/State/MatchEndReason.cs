#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// Why a single round of play ended.
    /// </summary>
    public enum MatchEndReason
    {
        /// <summary>A player emptied their hand ("went out", "domino").</summary>
        Domino,

        /// <summary>Every player passed in succession; no further play is possible.</summary>
        Blocked,
    }
}
