#nullable enable
using System.Collections.Generic;

namespace Pose.Core.Config
{
    /// <summary>
    /// Firebase Remote Config keys and their in-app defaults (M7 OTA, ADR 0021).
    /// The defaults are applied before the first remote fetch, so every key resolves
    /// to a sensible value offline and on first launch. Keys here are
    /// <em>non-authoritative</em> — flags, a maintenance kill-switch, a force-update
    /// gate, and client URLs; anything guarding a wallet/stat/entitlement stays
    /// server-authoritative in Cloud Functions.
    /// </summary>
    public static class RemoteConfigKeys
    {
        /// <summary>When true, the client shows a maintenance screen and blocks play.</summary>
        public const string KillSwitchEnabled = "kill_switch_enabled";

        /// <summary>Minimum build number allowed; below it the client prompts to update.</summary>
        public const string MinSupportedBuild = "min_supported_build";

        /// <summary>Master toggle for the Facebook login/social features.</summary>
        public const string FeatureFacebookEnabled = "feature_facebook_enabled";

        /// <summary>Toggle for the invite-a-friend reward flow.</summary>
        public const string FeatureInvitesEnabled = "feature_invites_enabled";

        /// <summary>Master switch for in-match voice chat (ADR 0024). Fails closed.</summary>
        public const string FeatureVoiceEnabled = "feature_voice_enabled";

        /// <summary>
        /// Comma-separated tables voice is switched on for — see
        /// <c>Pose.Core.Voice.VoiceRoomPolicy</c> for the tokens. The lever that
        /// widens voice past friends-only without a store build (ADR 0024 §5).
        /// </summary>
        public const string VoiceAllowedModes = "voice_allowed_modes";

        /// <summary>Terms of Service URL opened from the client.</summary>
        public const string TermsUrl = "terms_url";

        /// <summary>Privacy Policy URL opened from the client.</summary>
        public const string PrivacyUrl = "privacy_url";

        /// <summary>User-data-deletion URL opened from the client.</summary>
        public const string DataDeletionUrl = "data_deletion_url";

        /// <summary>
        /// In-app defaults applied before the first fetch. Numbers are stored as
        /// <c>long</c> to match Firebase Remote Config's integer value type.
        /// </summary>
        public static IReadOnlyDictionary<string, object> Defaults { get; } =
            new Dictionary<string, object>
            {
                [KillSwitchEnabled] = false,
                [MinSupportedBuild] = 0L,
                [FeatureFacebookEnabled] = true,
                [FeatureInvitesEnabled] = true,
                // Voice defaults OFF: a failed fetch must never open a
                // microphone the player did not expect. Scope defaults to
                // code-joined rooms only — strangers stay text-only until
                // moderation is proven (ADR 0024 §5).
                [FeatureVoiceEnabled] = false,
                [VoiceAllowedModes] = "private",
                [TermsUrl] = "https://caribbeandominos.com/terms",
                [PrivacyUrl] = "https://caribbeandominos.com/privacy",
                [DataDeletionUrl] = "https://caribbeandominos.com/data-deletion",
            };
    }
}
