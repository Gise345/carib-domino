#nullable enable

namespace Pose.Core.Chat
{
    /// <summary>
    /// Why a player is flagging a message. A closed set, mirroring the server's
    /// <c>REPORT_REASONS</c>, so the moderation queue stays filterable.
    /// </summary>
    public enum ChatReportReason
    {
        /// <summary>Personal abuse aimed at a player.</summary>
        Harassment = 0,

        /// <summary>Racist, homophobic or otherwise hateful language.</summary>
        Hate = 1,

        /// <summary>Threats of violence.</summary>
        Threats = 2,

        /// <summary>Sexual content, including anything aimed at a minor.</summary>
        Sexual = 3,

        /// <summary>Flooding, advertising, or link spam.</summary>
        Spam = 4,

        /// <summary>Collusion or cheating arranged in chat.</summary>
        Cheating = 5,

        /// <summary>Anything else — the note carries the detail.</summary>
        Other = 6,
    }
}
