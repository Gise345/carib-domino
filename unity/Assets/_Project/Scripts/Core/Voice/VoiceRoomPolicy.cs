#nullable enable
using System;

namespace Pose.Core.Voice
{
    /// <summary>
    /// Whether voice is switched on for a given table (ADR 0024 §5). Scope is
    /// data, not code: a master flag plus a comma-separated scope list, both from
    /// Remote Config, so voice widens without a store build (ADR 0021).
    ///
    /// Pure and unit-tested. The server re-checks entitlement when it mints a
    /// token, so a tampered client that forces this to <c>true</c> still cannot
    /// speak — but scope itself is a product decision, not a security boundary,
    /// which is why it is allowed to live client-side.
    /// </summary>
    public static class VoiceRoomPolicy
    {
        /// <summary>Code-joined rooms — the table is friends. The launch scope.</summary>
        public const string ScopePrivate = "private";

        /// <summary>
        /// Partner tables of ANY origin, including matchmade ones. A separate
        /// token because a silent partner breaks 2-v-2 play in a way a silent
        /// opponent does not — but it does expose players to strangers, so it is
        /// off at launch and exists as a lever to pull deliberately.
        /// </summary>
        public const string ScopePartner = "partner";

        /// <summary>Random matchmaking — strangers. Off at launch.</summary>
        public const string ScopeRandom = "random";

        /// <summary>
        /// Whether this table may use voice at all.
        /// </summary>
        /// <param name="featureEnabled">The <c>feature_voice_enabled</c> master flag.</param>
        /// <param name="allowedScopes">
        /// The <c>voice_allowed_modes</c> list, e.g. <c>"private,partner"</c>.
        /// Unknown tokens are ignored so a typo in Remote Config narrows voice
        /// rather than opening it.
        /// </param>
        /// <param name="origin">How the player reached this table.</param>
        /// <param name="mode">The ruleset being played.</param>
        /// <returns>True when voice is in scope here.</returns>
        public static bool IsAllowed(
            bool featureEnabled,
            string? allowedScopes,
            VoiceRoomOrigin origin,
            GameMode mode)
        {
            // Offline and bot play have nobody to talk to; the master flag is the
            // kill switch, and it fails closed.
            if (!featureEnabled || origin == VoiceRoomOrigin.None)
            {
                return false;
            }

            if (origin == VoiceRoomOrigin.PrivateCode && HasScope(allowedScopes, ScopePrivate))
            {
                return true;
            }

            if (mode == GameMode.Partner && HasScope(allowedScopes, ScopePartner))
            {
                return true;
            }

            return origin == VoiceRoomOrigin.RandomMatchmaking && HasScope(allowedScopes, ScopeRandom);
        }

        /// <summary>
        /// Whether a scope list contains a token, ignoring case and surrounding
        /// whitespace so <c>"private, partner"</c> works as typed in the console.
        /// </summary>
        /// <param name="allowedScopes">The raw Remote Config list.</param>
        /// <param name="scope">The token to look for.</param>
        /// <returns>True when the token is present.</returns>
        public static bool HasScope(string? allowedScopes, string scope)
        {
            if (string.IsNullOrWhiteSpace(allowedScopes))
            {
                return false;
            }

            foreach (string token in allowedScopes!.Split(','))
            {
                if (token.Trim().Equals(scope, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
