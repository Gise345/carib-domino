using NUnit.Framework;
using Pose.Core.Chat;
using Pose.Core.Voice;

namespace Pose.Core.Tests.Voice
{
    public class VoiceEntitlementTests
    {
        private static ChatEntitlement Chat(
            bool isSignedIn = true, bool isGuest = false, bool isMuted = false, bool hasRoom = true) =>
            ChatEntitlement.For(isSignedIn, isGuest, isMuted, hasRoom);

        [Test]
        public void RealAccountInAnAllowedRoomMaySpeak()
        {
            VoiceEntitlement voice = VoiceEntitlement.For(Chat(), roomAllowsVoice: true, MicPermissionState.Granted);

            Assert.IsTrue(voice.CanSpeak);
            Assert.IsTrue(voice.CanListen);
            Assert.AreEqual(VoiceLockReason.None, voice.LockReason);
        }

        [Test]
        public void GuestNeitherSpeaksNorListens()
        {
            // Voice parts company with chat here (ADR 0024 §3). A guest may READ
            // chat because text you can see is text you can report; a voice you
            // have no participant handle for cannot be reported at all. So a
            // guest never connects to the channel.
            VoiceEntitlement voice = VoiceEntitlement.For(
                Chat(isGuest: true), roomAllowsVoice: true, MicPermissionState.Granted);

            Assert.IsFalse(voice.CanSpeak);
            Assert.IsFalse(voice.CanListen, "a guest must never connect to the voice channel");
            Assert.AreEqual(VoiceLockReason.Guest, voice.LockReason);
        }

        [Test]
        public void ModeratorMuteTakesTheVoiceButNotTheEars()
        {
            VoiceEntitlement voice = VoiceEntitlement.For(
                Chat(isMuted: true), roomAllowsVoice: true, MicPermissionState.Granted);

            Assert.IsFalse(voice.CanSpeak);
            Assert.IsTrue(voice.CanListen, "a muted player still follows the table");
            Assert.AreEqual(VoiceLockReason.Muted, voice.LockReason);
        }

        [Test]
        public void SignedOutCanNeitherSpeakNorListen()
        {
            VoiceEntitlement voice = VoiceEntitlement.For(
                Chat(isSignedIn: false), roomAllowsVoice: true, MicPermissionState.Granted);

            Assert.IsFalse(voice.CanSpeak);
            Assert.IsFalse(voice.CanListen);
            Assert.AreEqual(VoiceLockReason.SignedOut, voice.LockReason);
        }

        [Test]
        public void NoRoomYetLocksWithoutBlamingThePlayer()
        {
            VoiceEntitlement voice = VoiceEntitlement.For(
                Chat(hasRoom: false), roomAllowsVoice: true, MicPermissionState.Granted);

            Assert.IsFalse(voice.CanSpeak);
            Assert.AreEqual(VoiceLockReason.NoRoom, voice.LockReason);
        }

        [Test]
        public void RefusedMicrophoneLocksSpeakingButNotListening()
        {
            VoiceEntitlement voice = VoiceEntitlement.For(Chat(), roomAllowsVoice: true, MicPermissionState.Denied);

            Assert.IsFalse(voice.CanSpeak);
            Assert.IsTrue(voice.CanListen, "a refused mic must not also cut the player off from the table");
            Assert.AreEqual(VoiceLockReason.MicPermissionDenied, voice.LockReason);
        }

        [Test]
        public void UnaskedMicrophoneStaysUnlockedSoThePromptCanComeAtFirstUse()
        {
            VoiceEntitlement voice = VoiceEntitlement.For(Chat(), roomAllowsVoice: true, MicPermissionState.Unknown);

            Assert.IsTrue(voice.CanSpeak);
            Assert.AreEqual(VoiceLockReason.None, voice.LockReason);
        }

        [Test]
        public void OutOfScopeRoomBeatsEveryOtherReason()
        {
            // A guest at a voice-less table should be told the table has no voice,
            // not sold an account that would not help them here.
            VoiceEntitlement guest = VoiceEntitlement.For(
                Chat(isGuest: true), roomAllowsVoice: false, MicPermissionState.Denied);

            Assert.IsFalse(guest.CanSpeak);
            Assert.IsFalse(guest.CanListen);
            Assert.AreEqual(VoiceLockReason.NotAllowedInThisRoom, guest.LockReason);
        }

        [Test]
        public void VoiceAndChatNeverDisagreeOnTheGuestRule()
        {
            // The promise ADR 0023 §3 made: one rule, evaluated once. If chat ever
            // lets a guest send, voice must let them speak, and vice versa.
            foreach (bool isGuest in new[] { false, true })
            {
                ChatEntitlement chat = Chat(isGuest: isGuest);
                VoiceEntitlement voice = VoiceEntitlement.For(chat, true, MicPermissionState.Granted);

                Assert.AreEqual(chat.CanSend, voice.CanSpeak, $"drifted for isGuest={isGuest}");
            }
        }
    }
}
