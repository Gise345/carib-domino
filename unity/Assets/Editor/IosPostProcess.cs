#nullable enable

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using System.IO;
using UnityEditor.iOS.Xcode;
#endif

namespace Pose.Build
{
    /// <summary>
    /// Patches the exported Xcode project's Info.plist with the keys App Store
    /// Connect requires before a build can reach TestFlight testers. Runs after the
    /// Facebook SDK's own post-processor (priority 999) so its keys can be verified.
    /// </summary>
    public static class IosPostProcess
    {
        /// <summary>
        /// Declares the app uses no non-exempt encryption. Pose's only cryptography is
        /// HTTPS to Firebase/Photon, which is exempt under Apple's own criteria.
        /// Without this key every TestFlight upload stalls on a manual compliance answer.
        /// </summary>
        private const string EncryptionExemptKey = "ITSAppUsesNonExemptEncryption";

        /// <summary>
        /// The Facebook SDK collects the IDFA (<c>advertiserIDCollectionEnabled</c> in
        /// FacebookSettings) to attribute Meta app-install ads (ADR 0019 / SETUP part C).
        /// Apple rejects any build that touches the IDFA without this prompt string.
        /// </summary>
        private const string TrackingUsageKey = "NSUserTrackingUsageDescription";

        private const string TrackingUsageDescription =
            "Pose uses this to show you the game to friends who'd enjoy it, and to " +
            "measure which ads bring in real players.";

        /// <summary>
        /// Voice chat (ADR 0024). Apple CRASHES an app that touches the microphone
        /// without this key — it is a hard failure, not a review warning. The copy
        /// says what we do and what we do not do, because "never recorded" is both
        /// true (reports are metadata-only) and the thing a reviewer looks for.
        /// </summary>
        private const string MicrophoneUsageKey = "NSMicrophoneUsageDescription";

        private const string MicrophoneUsageDescription =
            "Pose uses your microphone so you can talk to the other players at your " +
            "table. Your voice is never recorded or stored.";

        private const string FacebookAppIdKey = "FacebookAppID";

        [PostProcessBuild(1000)]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
        {
#if UNITY_IOS
            if (buildTarget != BuildTarget.iOS)
            {
                return;
            }

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            PlistElementDict root = plist.root;
            root.SetBoolean(EncryptionExemptKey, false);
            root.SetString(TrackingUsageKey, TrackingUsageDescription);
            root.SetString(MicrophoneUsageKey, MicrophoneUsageDescription);

            bool facebookConfigured = root.values.ContainsKey(FacebookAppIdKey);
            plist.WriteToFile(plistPath);

            if (facebookConfigured)
            {
                return;
            }

            Debug.LogWarning(
                $"{FacebookAppIdKey} is missing from the exported Info.plist. The Facebook " +
                "SDK normally writes it from FacebookSettings — check that the iOS platform " +
                "is registered on the Meta app and re-run Facebook ▸ Edit Settings. " +
                "Facebook login will fail on device until it is present.");
#endif
        }
    }
}
