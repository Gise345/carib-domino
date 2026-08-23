using NUnit.Framework;
using Pose.Core.Voice;

namespace Pose.Core.Tests.Voice
{
    public class VoiceRefusalTests
    {
        [Test]
        public void ReadsEachCodeOffTheMessagePrefix()
        {
            Assert.AreEqual(
                VoiceJoinOutcome.GuestRestricted,
                VoiceRefusal.Parse("guest-restricted: Create a free account to use chat and voice."));
            Assert.AreEqual(
                VoiceJoinOutcome.Muted,
                VoiceRefusal.Parse("muted: You are muted in chat."));
            Assert.AreEqual(
                VoiceJoinOutcome.VoiceDisabled,
                VoiceRefusal.Parse("voice-disabled: Voice is not available yet."));
            Assert.AreEqual(
                VoiceJoinOutcome.NotInRoom,
                VoiceRefusal.Parse("not-in-room: You are not in that room."));
            Assert.AreEqual(
                VoiceJoinOutcome.RateLimited,
                VoiceRefusal.Parse("rate-limited: Too many voice connections."));
        }

        [Test]
        public void FallsBackToTheSdkErrorCodeWhenThePrefixIsLost()
        {
            // Belt and braces: if a reworded server message ever drops its prefix,
            // a rate limit still has to be distinguishable from a hard failure.
            Assert.AreEqual(
                VoiceJoinOutcome.RateLimited,
                VoiceRefusal.Parse("Too many requests.", resourceExhausted: true));
        }

        [Test]
        public void UnknownOrMissingMessagesFailClosed()
        {
            Assert.AreEqual(VoiceJoinOutcome.Failed, VoiceRefusal.Parse(null));
            Assert.AreEqual(VoiceJoinOutcome.Failed, VoiceRefusal.Parse(string.Empty));
            Assert.AreEqual(VoiceJoinOutcome.Failed, VoiceRefusal.Parse("something went wrong"));
            Assert.AreEqual(VoiceJoinOutcome.Failed, VoiceRefusal.Parse("banned: suspended"));
        }

        [Test]
        public void EveryOutcomeMapsToALockAndOnlyOkIsUnlocked()
        {
            foreach (VoiceJoinOutcome outcome in System.Enum.GetValues(typeof(VoiceJoinOutcome)))
            {
                VoiceLockReason reason = VoiceRefusal.ToLockReason(outcome);

                bool expectUnlocked = outcome == VoiceJoinOutcome.Ok;
                Assert.AreEqual(
                    expectUnlocked,
                    reason == VoiceLockReason.None,
                    $"{outcome} mapped to {reason}");
            }
        }

        [Test]
        public void ServerRefusalRendersTheSameAsTheLocalLock()
        {
            // A guest refused by the server and a guest locked locally must look
            // identical to the player — same sign-up CTA either way.
            Assert.AreEqual(
                VoiceLockReason.Guest,
                VoiceRefusal.ToLockReason(VoiceJoinOutcome.GuestRestricted));
        }
    }
}
