#nullable enable

using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Pose.Build
{
    /// <summary>
    /// Applies every player setting a store build depends on, so that a build is
    /// reproducible from a clean clone without anyone touching the Inspector.
    /// Called by <see cref="BuildScript"/> before each build, and available from
    /// the Unity menu for day-to-day Editor use.
    /// </summary>
    public static class ReleaseBuildSettings
    {
        /// <summary>Marketing version, e.g. "0.1.0". Overrides <c>bundleVersion</c> when set.</summary>
        public const string VersionEnvVar = "POSE_BUILD_VERSION";

        /// <summary>Monotonic build number — Android <c>versionCode</c> and iOS <c>CFBundleVersion</c>.</summary>
        public const string BuildNumberEnvVar = "POSE_BUILD_NUMBER";

        public const string KeystorePathEnvVar = "POSE_KEYSTORE_PATH";
        public const string KeystorePassEnvVar = "POSE_KEYSTORE_PASS";
        public const string KeyAliasEnvVar = "POSE_KEY_ALIAS";
        public const string KeyPassEnvVar = "POSE_KEY_PASS";
        public const string AppleTeamIdEnvVar = "POSE_APPLE_TEAM_ID";

        private const string CompanyName = "Invovibe";
        private const string ProductName = "Pose";
        private const string BundleIdentifier = "com.invovibe.posedominoes";

        // Google Play requires new uploads to target the previous year's Android
        // release. API 36 (Android 16) is the level enforced from 2026-08-31.
        private const int AndroidTargetSdk = 36;

        // Android 7.1. Floor is set by the Firebase Android SDK (23) — we keep a
        // little headroom rather than tracking its minimum exactly.
        private const int AndroidMinSdk = 25;

        /// <summary>
        /// Applies shared identity settings plus the platform-specific settings for
        /// <paramref name="target"/>.
        /// </summary>
        /// <param name="target">The platform the build is being produced for.</param>
        public static void Apply(BuildTarget target)
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            string? version = ReadEnv(VersionEnvVar);
            if (!string.IsNullOrEmpty(version))
            {
                PlayerSettings.bundleVersion = version;
            }

            switch (target)
            {
                case BuildTarget.Android:
                    ApplyAndroid();
                    break;
                case BuildTarget.iOS:
                    ApplyIos();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(target), target, "Pose only ships Android and iOS.");
            }
        }

        [MenuItem("Pose/Apply Release Settings (Android)")]
        private static void ApplyAndroidFromMenu() => Apply(BuildTarget.Android);

        [MenuItem("Pose/Apply Release Settings (iOS)")]
        private static void ApplyIosFromMenu() => Apply(BuildTarget.iOS);

        private static void ApplyAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleIdentifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)AndroidMinSdk;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)AndroidTargetSdk;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

            int? buildNumber = ReadBuildNumber();
            if (buildNumber.HasValue)
            {
                PlayerSettings.Android.bundleVersionCode = buildNumber.Value;
            }

            ApplyAndroidSigning();
        }

        private static void ApplyAndroidSigning()
        {
            string? keystorePath = ReadEnv(KeystorePathEnvVar);
            if (string.IsNullOrEmpty(keystorePath))
            {
                // Debug-signed builds are still useful (sideload smoke tests); only
                // Play uploads need the upload key, and those always set the env.
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning(
                    $"{KeystorePathEnvVar} not set — building with the Android debug key. " +
                    "This artifact cannot be uploaded to Google Play.");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = RequireEnv(KeystorePassEnvVar);
            PlayerSettings.Android.keyaliasName = RequireEnv(KeyAliasEnvVar);
            PlayerSettings.Android.keyaliasPass = RequireEnv(KeyPassEnvVar);
        }

        private static void ApplyIos()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleIdentifier);

            int? buildNumber = ReadBuildNumber();
            if (buildNumber.HasValue)
            {
                PlayerSettings.iOS.buildNumber = buildNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            // Codemagic fetches the signing files and rewrites the exported Xcode
            // project with `xcode-project use-profiles`, which requires manual signing.
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;

            string? teamId = ReadEnv(AppleTeamIdEnvVar);
            if (!string.IsNullOrEmpty(teamId))
            {
                PlayerSettings.iOS.appleDeveloperTeamID = teamId;
            }
        }

        private static int? ReadBuildNumber()
        {
            string? raw = ReadEnv(BuildNumberEnvVar);
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                throw new BuildFailedException($"{BuildNumberEnvVar} must be an integer, got '{raw}'.");
            }

            return parsed;
        }

        private static string? ReadEnv(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string RequireEnv(string name)
        {
            return ReadEnv(name)
                   ?? throw new BuildFailedException($"{name} is required but was not set.");
        }
    }
}
