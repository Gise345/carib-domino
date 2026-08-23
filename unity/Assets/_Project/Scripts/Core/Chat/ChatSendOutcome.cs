#nullable enable

namespace Pose.Core.Chat
{
    /// <summary>
    /// What became of a send attempt. Each value the panel must react to
    /// differently gets its own case; everything else is <see cref="Failed"/>.
    /// </summary>
    public enum ChatSendOutcome
    {
        /// <summary>Delivered.</summary>
        Ok = 0,

        /// <summary>A guest tried to send — offer the account CTA.</summary>
        GuestRestricted = 1,

        /// <summary>A moderator mute is in force.</summary>
        Muted = 2,

        /// <summary>Sending too fast.</summary>
        RateLimited = 3,

        /// <summary>Network, validation, a ban, or anything else.</summary>
        Failed = 4,
    }
}
