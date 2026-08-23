using NUnit.Framework;
using Pose.Core.Chat;

namespace Pose.Core.Tests.Chat
{
    /// <summary>
    /// The client half of the refusal contract. Unity's FunctionsException drops
    /// the callable's structured details, so the code has to be read off the
    /// message prefix — and getting this wrong degrades every refusal into a
    /// generic error, which is exactly the failure the player would notice
    /// (a guest told "couldn't send" instead of being offered an account).
    /// </summary>
    public sealed class ChatRefusalTests
    {
        [Test]
        public void Parse_ReadsTheGuestCode()
        {
            ChatSendOutcome outcome = ChatRefusal.Parse(
                "guest-restricted: Create a free account to use chat and voice.");

            Assert.That(outcome, Is.EqualTo(ChatSendOutcome.GuestRestricted));
        }

        [Test]
        public void Parse_ReadsTheMuteCode()
        {
            Assert.That(
                ChatRefusal.Parse("muted: You are muted in chat."),
                Is.EqualTo(ChatSendOutcome.Muted));
        }

        [Test]
        public void Parse_ReadsTheRateLimitCode()
        {
            Assert.That(
                ChatRefusal.Parse("rate-limited: Slow down a moment."),
                Is.EqualTo(ChatSendOutcome.RateLimited));
        }

        [Test]
        public void Parse_FallsBackToTheSdkErrorCodeForRateLimits()
        {
            // If the prefix is ever lost, resource-exhausted still means one thing.
            Assert.That(
                ChatRefusal.Parse("Slow down.", resourceExhausted: true),
                Is.EqualTo(ChatSendOutcome.RateLimited));
        }

        [Test]
        public void Parse_TreatsAnUnknownRefusalAsAPlainFailure()
        {
            Assert.That(ChatRefusal.Parse("INTERNAL"), Is.EqualTo(ChatSendOutcome.Failed));
            Assert.That(ChatRefusal.Parse(null), Is.EqualTo(ChatSendOutcome.Failed));
            Assert.That(ChatRefusal.Parse(string.Empty), Is.EqualTo(ChatSendOutcome.Failed));
        }

        [Test]
        public void Parse_DoesNotMistakeASentenceForACode()
        {
            // A refusal with no code, whose message happens to contain a colon.
            Assert.That(
                ChatRefusal.Parse("This account is suspended: contact support."),
                Is.EqualTo(ChatSendOutcome.Failed));
        }

        [Test]
        public void CodeOf_ReturnsTheBareCode()
        {
            Assert.That(ChatRefusal.CodeOf("muted: nope"), Is.EqualTo("muted"));
            Assert.That(ChatRefusal.CodeOf("no code here"), Is.Empty);
            Assert.That(ChatRefusal.CodeOf(": leading colon"), Is.Empty);
        }

        [Test]
        public void Codes_MatchTheServerConstants()
        {
            // These strings are the wire contract with functions/src/chat/refusals.ts.
            Assert.That(ChatRefusal.GuestCode, Is.EqualTo("guest-restricted"));
            Assert.That(ChatRefusal.MutedCode, Is.EqualTo("muted"));
            Assert.That(ChatRefusal.RateLimitedCode, Is.EqualTo("rate-limited"));
        }
    }
}
