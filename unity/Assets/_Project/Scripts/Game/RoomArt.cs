#nullable enable
using System;
using UnityEngine;

namespace Pose.Game
{
    /// <summary>
    /// The painted art the three game rooms wear, gathered into one inspector
    /// block so <see cref="BoardBootstrap"/> hands it over in a single call
    /// rather than six setters that drift out of step.
    ///
    /// Every field is optional. A room with no art draws lettered stand-ins and
    /// still reads correctly, which is what lets the art land one file at a time
    /// instead of blocking the screen until all six exist.
    ///
    /// Supply each PNG with <b>real transparency</b> and trimmed to its own
    /// bounds — no white background, no empty margin. The board in particular is
    /// positioned by fractions of its own rect, so a transparent margin would
    /// push the stake numbers off the plank.
    /// </summary>
    [Serializable]
    public sealed class RoomArt
    {
        [Tooltip("The room ground — the beach scene behind all three rooms. " +
                 "Drawn at the 800x1730 canvas ratio and stretched to fill; a " +
                 "different ratio will be squashed rather than cropped.")]
        public Sprite? RoomBackground;

        [Tooltip("Cut Throat Online title. Carries the top of the room in place " +
                 "of a text header. Transparent PNG, trimmed.")]
        public Sprite? CutThroatTitle;

        [Tooltip("Partner title. Same proportions as Cut Throat so the two " +
                 "heroes line up. Transparent PNG, trimmed.")]
        public Sprite? PartnerTitle;

        [Tooltip("One-Love With Friends title. A wider, shorter banner than the " +
                 "other two — the hero sizes itself to suit. Transparent PNG.")]
        public Sprite? OneLoveTitle;

        [Tooltip("Classic 6 Love format tile. A cut-out that floats on the " +
                 "tile ground rather than filling it. Transparent PNG.")]
        public Sprite? ClassicTile;

        [Tooltip("Quick Love format tile. Sits beside Classic, so it should " +
                 "match it for lighting and weight. Transparent PNG.")]
        public Sprite? QuickTile;

        [Tooltip("The carved rewards board. A frame, not a picture: its plank " +
                 "is left empty and the stake numbers are set inside it, clear " +
                 "of the flowers and the chest. Transparent PNG, trimmed.")]
        public Sprite? RewardsBoard;
    }
}
