using NUnit.Framework;
using Pose.Core.Chat;

namespace Pose.Core.Tests.Chat
{
    /// <summary>
    /// Which room ids are worth showing a player. A matchmade session id shown
    /// as a "table code" is what produced <c>Table dl3010f3-ec3a-4f81…</c> in
    /// the header — meaningless to the player, and unjoinable besides.
    /// </summary>
    public sealed class ChatRoomLabelTests
    {
        [Test]
        public void IsJoinableCode_AcceptsAGeneratedTableCode()
        {
            Assert.That(ChatRoomLabel.IsJoinableCode("7KQ2MP"), Is.True);
            Assert.That(ChatRoomLabel.IsJoinableCode("ABCDEF"), Is.True);
        }

        [Test]
        public void IsJoinableCode_RejectsAMatchmadeSessionId()
        {
            Assert.That(ChatRoomLabel.IsJoinableCode("dl3010f3-ec3a-4f81-b4c1-3a132a713535"), Is.False);
        }

        [Test]
        public void IsJoinableCode_RejectsTheWrongLength()
        {
            Assert.That(ChatRoomLabel.IsJoinableCode("7KQ2M"), Is.False);
            Assert.That(ChatRoomLabel.IsJoinableCode("7KQ2MPX"), Is.False);
            Assert.That(ChatRoomLabel.IsJoinableCode(string.Empty), Is.False);
            Assert.That(ChatRoomLabel.IsJoinableCode(null), Is.False);
        }

        [Test]
        public void IsJoinableCode_RejectsCharactersTheGeneratorNeverUses()
        {
            // I, O, 0 and 1 are left out so a code can be read aloud; anything
            // containing them did not come from the generator.
            Assert.That(ChatRoomLabel.IsJoinableCode("7KQ2M0"), Is.False);
            Assert.That(ChatRoomLabel.IsJoinableCode("7KQ2MI"), Is.False);
            Assert.That(ChatRoomLabel.IsJoinableCode("7KQ2-P"), Is.False);
        }

        [Test]
        public void DisplayCode_UppercasesACodeAndDropsEverythingElse()
        {
            Assert.That(ChatRoomLabel.DisplayCode("7kq2mp"), Is.EqualTo("7KQ2MP"));
            Assert.That(ChatRoomLabel.DisplayCode("dl3010f3-ec3a"), Is.Null);
            Assert.That(ChatRoomLabel.DisplayCode(null), Is.Null);
        }
    }
}
