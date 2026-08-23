#nullable enable
using UnityEngine;

namespace Pose.Game
{
    /// <summary>
    /// Player-facing toggles persisted across launches via PlayerPrefs.
    /// Currently a single setting: tap-mode for the player's own tiles —
    /// 1-tap plays immediately, 2-tap selects (lift + highlight) on first
    /// tap and confirms on second. Pass and Resign always stay 1-tap
    /// regardless of this setting; the choice only affects regular tile
    /// placements.
    /// </summary>
    public static class GameSettings
    {
        private const string TapModeKey = "Pose.TapMode";

        // Default: 2-tap (Giselle's preference after the M3.5 playtest where
        // mistaken single-taps mis-played tiles).
        private const int DefaultTapMode = 2;

        public enum TapMode
        {
            OneTap = 1,
            TwoTap = 2,
        }

        public static TapMode CurrentTapMode
        {
            get => (TapMode)PlayerPrefs.GetInt(TapModeKey, DefaultTapMode);
            set
            {
                PlayerPrefs.SetInt(TapModeKey, (int)value);
                PlayerPrefs.Save();
                TileView.TwoTapModeStatic = value == TapMode.TwoTap;
            }
        }

        // ---- Volumes -------------------------------------------------------
        //
        // Stored 0..1. Sliders rather than on/off because the reason to open
        // Settings is almost always "quieter", rarely "silent" — and a player
        // who can only mute tends to mute and never come back.

        private const string EffectsKey = "Pose.Volume.Effects";
        private const string MusicKey = "Pose.Volume.Music";
        private const string NotificationsKey = "Pose.Volume.Notifications";

        /// <summary>Tile taps, slams, round stings. 0..1.</summary>
        public static float EffectsVolume
        {
            get => PlayerPrefs.GetFloat(EffectsKey, 0.8f);
            set => SetVolume(EffectsKey, value);
        }

        /// <summary>Background music. 0..1. Quieter by default than effects.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 0.45f);
            set => SetVolume(MusicKey, value);
        }

        /// <summary>
        /// Push alerts and in-app chimes. 0..1. Does NOT govern the turn-timer
        /// nudge, which is a haptic and deliberately not mutable — see
        /// <see cref="Haptics.Nudge"/>.
        /// </summary>
        public static float NotificationVolume
        {
            get => PlayerPrefs.GetFloat(NotificationsKey, 0.6f);
            set => SetVolume(NotificationsKey, value);
        }

        private static void SetVolume(string key, float value01)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(value01));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Read PlayerPrefs and push the value into <see cref="TileView"/>'s
        /// static slot. Called once at boot from <see cref="BoardBootstrap.Start"/>
        /// so TileView reflects the persisted setting before the first hand
        /// renders.
        /// </summary>
        public static void Apply()
        {
            TileView.TwoTapModeStatic = CurrentTapMode == TapMode.TwoTap;
        }
    }
}
