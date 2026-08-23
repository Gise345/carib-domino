using NUnit.Framework;
using Pose.Core;
using Pose.Core.Voice;

namespace Pose.Core.Tests.Voice
{
    public class VoiceRoomPolicyTests
    {
        private const string FriendsOnly = "private";
        private const string Everything = "private,partner,random";

        [Test]
        public void MasterFlagOffLocksEveryTable()
        {
            bool allowed = VoiceRoomPolicy.IsAllowed(
                featureEnabled: false,
                allowedScopes: Everything,
                VoiceRoomOrigin.PrivateCode,
                GameMode.Partner);

            Assert.IsFalse(allowed, "the kill switch must beat any scope list");
        }

        [Test]
        public void PrivateCodeRoomIsAllowedAtLaunchScope()
        {
            bool allowed = VoiceRoomPolicy.IsAllowed(
                featureEnabled: true,
                FriendsOnly,
                VoiceRoomOrigin.PrivateCode,
                GameMode.CutThroat);

            Assert.IsTrue(allowed);
        }

        [Test]
        public void RandomMatchmakingIsLockedAtLaunchScope()
        {
            // ADR 0024 §5 — voice leaves no transcript, so strangers stay text-only.
            bool allowed = VoiceRoomPolicy.IsAllowed(
                featureEnabled: true,
                FriendsOnly,
                VoiceRoomOrigin.RandomMatchmaking,
                GameMode.CutThroat);

            Assert.IsFalse(allowed);
        }

        [Test]
        public void MatchmadePartnerStaysLockedUntilThePartnerScopeIsAdded()
        {
            bool atLaunch = VoiceRoomPolicy.IsAllowed(
                featureEnabled: true,
                FriendsOnly,
                VoiceRoomOrigin.RandomMatchmaking,
                GameMode.Partner);

            bool widened = VoiceRoomPolicy.IsAllowed(
                featureEnabled: true,
                "private,partner",
                VoiceRoomOrigin.RandomMatchmaking,
                GameMode.Partner);

            Assert.IsFalse(atLaunch, "matchmade Partner is still strangers");
            Assert.IsTrue(widened, "the partner scope is the lever that opens it");
        }

        [Test]
        public void OfflineAndBotPlayNeverGetVoice()
        {
            bool allowed = VoiceRoomPolicy.IsAllowed(
                featureEnabled: true,
                Everything,
                VoiceRoomOrigin.None,
                GameMode.CutThroat);

            Assert.IsFalse(allowed, "there is nobody to talk to");
        }

        [Test]
        public void ScopeListToleratesConsoleTypingButNotTypos()
        {
            Assert.IsTrue(VoiceRoomPolicy.HasScope("private, partner", VoiceRoomPolicy.ScopePartner));
            Assert.IsTrue(VoiceRoomPolicy.HasScope("PRIVATE", VoiceRoomPolicy.ScopePrivate));
            Assert.IsTrue(VoiceRoomPolicy.HasScope("  private  ", VoiceRoomPolicy.ScopePrivate));

            // A typo must narrow voice, never open it.
            Assert.IsFalse(VoiceRoomPolicy.HasScope("privat", VoiceRoomPolicy.ScopePrivate));
            Assert.IsFalse(VoiceRoomPolicy.HasScope("randomly", VoiceRoomPolicy.ScopeRandom));
        }

        [Test]
        public void EmptyOrMissingScopeListLocksEverything()
        {
            Assert.IsFalse(VoiceRoomPolicy.HasScope(null, VoiceRoomPolicy.ScopePrivate));
            Assert.IsFalse(VoiceRoomPolicy.HasScope(string.Empty, VoiceRoomPolicy.ScopePrivate));
            Assert.IsFalse(VoiceRoomPolicy.HasScope("   ", VoiceRoomPolicy.ScopePrivate));

            Assert.IsFalse(VoiceRoomPolicy.IsAllowed(
                featureEnabled: true,
                allowedScopes: null,
                VoiceRoomOrigin.PrivateCode,
                GameMode.CutThroat));
        }
    }
}
