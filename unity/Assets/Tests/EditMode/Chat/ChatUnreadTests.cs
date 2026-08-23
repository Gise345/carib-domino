using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pose.Core.Chat;

namespace Pose.Core.Tests.Chat
{
    /// <summary>
    /// The unread badge. Its whole job is to be trustworthy: a count that is
    /// wrong once teaches the player to ignore it, and then chat stays open over
    /// the board again.
    /// </summary>
    public sealed class ChatUnreadTests
    {
        private const string Me = "uid-me";
        private const string Them = "uid-them";

        private static ChatMessage Msg(string id, string sender) =>
            new(id, sender, "Someone", 1, "text", false, false, DateTime.UtcNow);

        private static List<ChatMessage> Thread() => new()
        {
            Msg("m1", Them),
            Msg("m2", Me),
            Msg("m3", Them),
            Msg("m4", Them),
        };

        [Test]
        public void Count_TreatsEverythingAsUnreadBeforeTheFirstOpen()
        {
            int unread = ChatUnread.Count(Thread(), lastSeenId: null, localUid: Me);

            Assert.That(unread, Is.EqualTo(3)); // m1, m3, m4 — not the player's own
        }

        [Test]
        public void Count_CountsOnlyWhatArrivedAfterTheMarker()
        {
            int unread = ChatUnread.Count(Thread(), lastSeenId: "m2", localUid: Me);

            Assert.That(unread, Is.EqualTo(2));
        }

        [Test]
        public void Count_IsZeroWhenTheMarkerIsTheLastMessage()
        {
            int unread = ChatUnread.Count(Thread(), lastSeenId: "m4", localUid: Me);

            Assert.That(unread, Is.Zero);
        }

        [Test]
        public void Count_NeverCountsThePlayersOwnMessages()
        {
            List<ChatMessage> onlyMine = new() { Msg("m1", Me), Msg("m2", Me) };

            Assert.That(ChatUnread.Count(onlyMine, null, Me), Is.Zero);
        }

        [Test]
        public void Count_FallsBackToEverythingWhenTheMarkerHasAgedOut()
        {
            // The listener keeps a 100-message window; a marker older than that
            // is gone, and showing the whole window is better than showing zero.
            int unread = ChatUnread.Count(Thread(), lastSeenId: "swept-away", localUid: Me);

            Assert.That(unread, Is.EqualTo(3));
        }

        [Test]
        public void Count_HandlesAnEmptyRoom()
        {
            Assert.That(ChatUnread.Count(new List<ChatMessage>(), null, Me), Is.Zero);
        }

        [Test]
        public void Count_CountsEverythingForASignedOutViewer()
        {
            Assert.That(ChatUnread.Count(Thread(), null, localUid: null), Is.EqualTo(4));
        }

        [Test]
        public void Badge_SaysNothingWhenThereIsNothingToSay()
        {
            Assert.That(ChatUnread.Badge(0), Is.Empty);
            Assert.That(ChatUnread.Badge(-1), Is.Empty);
        }

        [Test]
        public void Badge_CapsSoItCannotOutgrowTheButton()
        {
            Assert.That(ChatUnread.Badge(3), Is.EqualTo("3"));
            Assert.That(ChatUnread.Badge(9), Is.EqualTo("9"));
            Assert.That(ChatUnread.Badge(10), Is.EqualTo("9+"));
            Assert.That(ChatUnread.Badge(240), Is.EqualTo("9+"));
        }
    }
}
