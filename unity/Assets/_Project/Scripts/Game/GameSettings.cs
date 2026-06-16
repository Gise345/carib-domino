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
