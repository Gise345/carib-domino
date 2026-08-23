using NUnit.Framework;
using Pose.Core.Chat;

namespace Pose.Core.Tests.Chat
{
    /// <summary>
    /// The guest policy (ADR 0023 §3) in one place. These assertions are the
    /// client half of a rule the server also enforces — if the two ever
    /// disagree, the server wins and the player sees a composer that refuses
    /// them, which is the failure this suite exists to catch.
    /// </summary>
    public sealed class ChatEntitlementTests
    {
        [Test]
        public void SignedOut_CannotEvenRead()
        {
            ChatEntitlement e = ChatEntitlement.For(isSignedIn: false, isGuest: false, isMuted: false, hasRoom: true);

            Assert.That(e.CanRead, Is.False);
            Assert.That(e.CanSend, Is.False);
            Assert.That(e.LockReason, Is.EqualTo(ChatLockReason.SignedOut));
        }

        [Test]
        public void Guest_ReadsButCannotSend()
        {
            ChatEntitlement e = ChatEntitlement.For(isSignedIn: true, isGuest: true, isMuted: false, hasRoom: true);

            Assert.That(e.CanRead, Is.True);
            Assert.That(e.CanSend, Is.False);
            Assert.That(e.LockReason, Is.EqualTo(ChatLockReason.Guest));
        }

        [Test]
        public void Guest_CannotUseVoiceEither()
        {
            ChatEntitlement e = ChatEntitlement.For(isSignedIn: true, isGuest: true, isMuted: false, hasRoom: true);

            Assert.That(e.CanUseVoice, Is.False);
        }

        [Test]
        public void Guest_StaysLockedEvenWithoutAMuteOrRoom()
        {
            // Guest is the reason shown, not "no room": the sign-up CTA is the
            // useful thing to offer, and it is true regardless of the room.
            ChatEntitlement e = ChatEntitlement.For(isSignedIn: true, isGuest: true, isMuted: false, hasRoom: false);

            Assert.That(e.LockReason, Is.EqualTo(ChatLockReason.Guest));
        }

        [Test]
        public void MutedAccount_ReadsButCannotSend()
        {
            ChatEntitlement e = ChatEntitlement.For(isSignedIn: true, isGuest: false, isMuted: true, hasRoom: true);

            Assert.That(e.CanRead, Is.True);
            Assert.That(e.CanSend, Is.False);
            Assert.That(e.CanUseVoice, Is.False);
            Assert.That(e.LockReason, Is.EqualTo(ChatLockReason.Muted));
        }

        [Test]
        public void AccountWithoutARoom_HasNowhereToSend()
        {
            ChatEntitlement e = ChatEntitlement.For(isSignedIn: true, isGuest: false, isMuted: false, hasRoom: false);

            Assert.That(e.CanSend, Is.False);
            Assert.That(e.LockReason, Is.EqualTo(ChatLockReason.NoRoom));
        }

        [Test]
        public void SignedInAccountInARoom_HasEverything()
        {
            ChatEntitlement e = ChatEntitlement.For(isSignedIn: true, isGuest: false, isMuted: false, hasRoom: true);

            Assert.That(e.CanRead, Is.True);
            Assert.That(e.CanSend, Is.True);
            Assert.That(e.CanUseVoice, Is.True);
            Assert.That(e.LockReason, Is.EqualTo(ChatLockReason.None));
        }
    }
}
