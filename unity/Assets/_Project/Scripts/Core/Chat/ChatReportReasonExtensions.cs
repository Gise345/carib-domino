#nullable enable
using System;

namespace Pose.Core.Chat
{
    /// <summary>
    /// Wire conversion for <see cref="ChatReportReason"/>. The server validates
    /// against its own enum, so these strings must match
    /// <c>REPORT_REASONS</c> in <c>functions/src/chat/model.ts</c> exactly.
    /// </summary>
    public static class ChatReportReasonExtensions
    {
        /// <summary>The wire value the <c>reportChatMessage</c> callable expects.</summary>
        /// <param name="reason">The reason chosen in the report sheet.</param>
        /// <returns>The lowercase wire string.</returns>
        public static string ToWire(this ChatReportReason reason) => reason switch
        {
            ChatReportReason.Harassment => "harassment",
            ChatReportReason.Hate => "hate",
            ChatReportReason.Threats => "threats",
            ChatReportReason.Sexual => "sexual",
            ChatReportReason.Spam => "spam",
            ChatReportReason.Cheating => "cheating",
            ChatReportReason.Other => "other",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped report reason."),
        };

        /// <summary>The localization key for the reason's label in the report sheet.</summary>
        /// <param name="reason">The reason to label.</param>
        /// <returns>A <c>GameStrings</c> key.</returns>
        public static string LocalizationKey(this ChatReportReason reason) =>
            $"chat_report_reason_{reason.ToWire()}";
    }
}
