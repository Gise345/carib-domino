using NUnit.Framework;
using Pose.Core.Config;

namespace Pose.Core.Tests.Config
{
    public class RemoteConfigKeysTests
    {
        private static readonly string[] AllKeys =
        {
            RemoteConfigKeys.KillSwitchEnabled,
            RemoteConfigKeys.MinSupportedBuild,
            RemoteConfigKeys.FeatureFacebookEnabled,
            RemoteConfigKeys.FeatureInvitesEnabled,
            RemoteConfigKeys.TermsUrl,
            RemoteConfigKeys.PrivacyUrl,
            RemoteConfigKeys.DataDeletionUrl,
        };

        [Test]
        public void EveryDeclaredKeyHasExactlyOneDefault()
        {
            foreach (string key in AllKeys)
            {
                Assert.IsTrue(RemoteConfigKeys.Defaults.ContainsKey(key), $"missing default for {key}");
            }

            Assert.AreEqual(AllKeys.Length, RemoteConfigKeys.Defaults.Count, "defaults and declared keys diverge");
        }

        [Test]
        public void NumericDefaultsAreLongSoRemoteConfigTypesMatch()
        {
            // Firebase Remote Config integers are long; a boxed int here would read
            // back wrong via ConfigValue.LongValue.
            Assert.IsInstanceOf<long>(RemoteConfigKeys.Defaults[RemoteConfigKeys.MinSupportedBuild]);
        }

        [Test]
        public void FlagDefaultsFailSafe()
        {
            Assert.IsFalse(
                (bool)RemoteConfigKeys.Defaults[RemoteConfigKeys.KillSwitchEnabled],
                "kill switch must default OFF so a failed fetch never bricks the app");
            Assert.IsTrue((bool)RemoteConfigKeys.Defaults[RemoteConfigKeys.FeatureFacebookEnabled]);
            Assert.IsTrue((bool)RemoteConfigKeys.Defaults[RemoteConfigKeys.FeatureInvitesEnabled]);
        }

        [Test]
        public void LegalUrlDefaultsPointToTheLiveDomain()
        {
            Assert.That(
                (string)RemoteConfigKeys.Defaults[RemoteConfigKeys.TermsUrl],
                Does.StartWith("https://caribbeandominos.com/"));
            Assert.That(
                (string)RemoteConfigKeys.Defaults[RemoteConfigKeys.PrivacyUrl],
                Does.StartWith("https://caribbeandominos.com/"));
            Assert.That(
                (string)RemoteConfigKeys.Defaults[RemoteConfigKeys.DataDeletionUrl],
                Does.StartWith("https://caribbeandominos.com/"));
        }
    }
}
