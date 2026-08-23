using System;
using NUnit.Framework;
using Pose.Core.Chat;

namespace Pose.Core.Tests.Chat
{
    /// <summary>
    /// The message model and the wire values a report is filed with. The wire
    /// strings must match `REPORT_REASONS` in the Cloud Function exactly — the
    /// server validates against its own enum and rejects anything else, so a
    /// typo here would silently break reporting.
    /// </summary>
    public sealed class ChatMessageTests
    {
        private static ChatMessage Message(string senderUid) =>
            new("msg1", senderUid, "Sly Mongoose", 0, "hello", false, false, DateTime.UtcNow);

        [Test]
        public void IsFrom_MatchesTheSender()
        {
            Assert.That(Message("uid-a").IsFrom("uid-a"), Is.True);
        }

        [Test]
        public void IsFrom_RejectsAnotherPlayer()
        {
            Assert.That(Message("uid-a").IsFrom("uid-b"), Is.False);
        }

        [Test]
        public void IsFrom_IsFalseWhenSignedOut()
        {
            // A signed-out viewer owns nothing, so every message stays reportable
            // rather than being mistaken for their own.
            Assert.That(Message("uid-a").IsFrom(null), Is.False);
            Assert.That(Message("uid-a").IsFrom(string.Empty), Is.False);
        }

        [Test]
        public void IsFrom_IsCaseSensitive()
        {
            // Firebase uids are case-sensitive; a loose compare could hide the
            // report control on someone else's message.
            Assert.That(Message("uid-a").IsFrom("UID-A"), Is.False);
        }

        [Test]
        public void ReportReasons_MapToTheServersWireValues()
        {
            Assert.That(ChatReportReason.Harassment.ToWire(), Is.EqualTo("harassment"));
            Assert.That(ChatReportReason.Hate.ToWire(), Is.EqualTo("hate"));
            Assert.That(ChatReportReason.Threats.ToWire(), Is.EqualTo("threats"));
            Assert.That(ChatReportReason.Sexual.ToWire(), Is.EqualTo("sexual"));
            Assert.That(ChatReportReason.Spam.ToWire(), Is.EqualTo("spam"));
            Assert.That(ChatReportReason.Cheating.ToWire(), Is.EqualTo("cheating"));
            Assert.That(ChatReportReason.Other.ToWire(), Is.EqualTo("other"));
        }

        [Test]
        public void ReportReasons_HaveALocalizationKeyEach()
        {
            foreach (ChatReportReason reason in Enum.GetValues(typeof(ChatReportReason)))
            {
                Assert.That(reason.LocalizationKey(), Is.EqualTo($"chat_report_reason_{reason.ToWire()}"));
            }
        }
    }
}
