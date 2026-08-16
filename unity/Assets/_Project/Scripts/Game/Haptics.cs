#nullable enable
using UnityEngine;

namespace Pose.Game
{
    /// <summary>
    /// Device vibration. Two deliberately separate entry points, because they
    /// answer to different rules:
    ///
    /// <list type="bullet">
    ///   <item><see cref="Nudge"/> — the turn-timer prod. Fires unconditionally.
    ///         It is the game telling a stalling player that the table is waiting
    ///         on them, so it is <b>not</b> user-disableable: a player who muted
    ///         it could hold up three other people with no feedback that they
    ///         were doing so.</item>
    ///   <item><see cref="Feedback"/> — everything else (tile placement, button
    ///         confirmations, the slam in ADR 0017). Respects the player's
    ///         vibration setting.</item>
    /// </list>
    ///
    /// <c>Handheld.Vibrate</c> is Unity's built-in, no-dependency vibration: a
    /// fixed ~500 ms buzz on Android and the standard system vibrate on iOS. It
    /// takes no duration or intensity, so patterned haptics would need a native
    /// plugin — worth revisiting when the slam lands, not before. On desktop and
    /// in the Editor it is a no-op, so the nudge is silent when playtesting in
    /// Play mode; test it on the sideloaded APK.
    /// </summary>
    public static class Haptics
    {
        /// <summary>
        /// PlayerPrefs key for the user-facing vibration toggle. The Settings
        /// slice owns the UI; this reads the same key so the two can't drift.
        /// </summary>
        public const string VibrationEnabledKey = "Pose.VibrationEnabled";

        /// <summary>
        /// True when the player has vibration switched on. Defaults to on.
        /// Governs <see cref="Feedback"/> only — never <see cref="Nudge"/>.
        /// </summary>
        public static bool VibrationEnabled
        {
            get => PlayerPrefs.GetInt(VibrationEnabledKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(VibrationEnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Buzzes the phone because the player has stalled on their turn.
        /// Intentionally ignores <see cref="VibrationEnabled"/>.
        /// </summary>
        public static void Nudge()
        {
            Vibrate();
        }

        /// <summary>
        /// Buzzes the phone for an ordinary game event, if the player has
        /// vibration enabled.
        /// </summary>
        public static void Feedback()
        {
            if (!VibrationEnabled)
            {
                return;
            }
            Vibrate();
        }

        private static void Vibrate()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (!Application.isEditor)
            {
                Handheld.Vibrate();
            }
#endif
        }
    }
}
