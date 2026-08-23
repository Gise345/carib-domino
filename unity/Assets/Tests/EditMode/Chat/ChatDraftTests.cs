using NUnit.Framework;
using Pose.Core.Chat;

namespace Pose.Core.Tests.Chat
{
    /// <summary>
    /// The composer's local rules. They exist to spare a round trip, so what
    /// matters is that they agree with the server's `normalizeMessageText` —
    /// a draft the client calls sendable must not come back refused.
    /// </summary>
    public sealed class ChatDraftTests
    {
        [Test]
        public void Normalize_CollapsesWhitespace()
        {
            string result = ChatDraft.Normalize("  good   luck  ");

            Assert.That(result, Is.EqualTo("good luck"));
        }

        [Test]
        public void Normalize_FlattensNewlinePadding()
        {
            string result = ChatDraft.Normalize("hey\n\n\n\n\nyou");

            Assert.That(result, Is.EqualTo("hey you"));
        }

        [Test]
        public void Normalize_StripsControlCharacters()
        {
            string result = ChatDraft.Normalize("nice\u0007\u0000play");

            Assert.That(result, Is.EqualTo("nice play"));
        }

        [Test]
        public void Normalize_HandlesNullAndEmpty()
        {
            Assert.That(ChatDraft.Normalize(null), Is.Empty);
            Assert.That(ChatDraft.Normalize(string.Empty), Is.Empty);
            Assert.That(ChatDraft.Normalize("   \t  "), Is.Empty);
        }

        [Test]
        public void Normalize_KeepsAccentsAndPunctuation()
        {
            string result = ChatDraft.Normalize("¡Buena jugada, compadre!");

            Assert.That(result, Is.EqualTo("¡Buena jugada, compadre!"));
        }

        [Test]
        public void IsSendable_RejectsWhitespaceOnlyDraft()
        {
            Assert.That(ChatDraft.IsSendable("      "), Is.False);
            Assert.That(ChatDraft.IsSendable(null), Is.False);
        }

        [Test]
        public void IsSendable_AcceptsOrdinaryMessage()
        {
            Assert.That(ChatDraft.IsSendable(" good game "), Is.True);
        }

        [Test]
        public void IsSendable_AcceptsExactlyTheLimit()
        {
            string atLimit = new('a', ChatLimits.MaxMessageLength);

            Assert.That(ChatDraft.IsSendable(atLimit), Is.True);
        }

        [Test]
        public void IsSendable_RejectsOneOverTheLimit()
        {
            string tooLong = new('a', ChatLimits.MaxMessageLength + 1);

            Assert.That(ChatDraft.IsSendable(tooLong), Is.False);
        }

        [Test]
        public void IsSendable_MeasuresAfterNormalisation()
        {
            // Padding is stripped before the length is judged, so a draft the
            // server would accept isn't blocked locally on raw length.
            string padded = "   " + new string('a', ChatLimits.MaxMessageLength) + "   ";

            Assert.That(ChatDraft.IsSendable(padded), Is.True);
        }

        [Test]
        public void Remaining_CountsDownAndGoesNegativeWhenOver()
        {
            Assert.That(ChatDraft.Remaining(string.Empty), Is.EqualTo(ChatLimits.MaxMessageLength));
            Assert.That(ChatDraft.Remaining("abc"), Is.EqualTo(ChatLimits.MaxMessageLength - 3));
            Assert.That(ChatDraft.Remaining(new string('a', ChatLimits.MaxMessageLength + 5)), Is.EqualTo(-5));
        }
    }
}
