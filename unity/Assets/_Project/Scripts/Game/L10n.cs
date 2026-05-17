#nullable enable
using UnityEngine.Localization.Settings;

namespace Pose.Game
{
    /// <summary>
    /// Thin façade over Unity Localization's string database for lookups
    /// against the project's <c>GameStrings</c> table. Synchronous — relies on
    /// the table being preloaded by the time UI strings are first requested
    /// (Localization Settings → Preload Behavior: PreloadAllTables, set in the
    /// editor). Falls back to the entry's English value if the active locale
    /// has a missing translation.
    /// </summary>
    public static class L10n
    {
        public const string TableName = "GameStrings";

        public static string Get(string key)
        {
            return LocalizationSettings.StringDatabase.GetLocalizedString(TableName, key);
        }

        public static string Get(string key, params object[] arguments)
        {
            return LocalizationSettings.StringDatabase.GetLocalizedString(
                TableName,
                key,
                arguments);
        }
    }
}
