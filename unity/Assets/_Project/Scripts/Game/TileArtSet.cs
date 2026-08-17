#nullable enable
using UnityEngine;

namespace Pose.Game
{
    /// <summary>
    /// The sprites a domino is drawn from. A ScriptableObject is a Unity asset
    /// that holds data rather than living on a GameObject — it exists as a file
    /// in the project, so the art can be swapped without touching a scene or
    /// any code, which is what makes the regional skins in
    /// <c>docs/DECISIONS/0014-art-direction.md</c> cheap to add later.
    ///
    /// Deliberately three pieces rather than one image per tile. Composing a
    /// body, a divider and a repeated pip covers every tile in every set —
    /// double-six through double-twelve — at any size and either orientation,
    /// where whole-tile renders would need 28, 55 or 91 images per skin.
    ///
    /// Every field is optional. Anything left empty falls back to the
    /// procedural drawing <see cref="TileView"/> has always done, so a
    /// half-filled set degrades one piece at a time instead of breaking.
    /// </summary>
    [CreateAssetMenu(fileName = "TileArtSet", menuName = "Pose/Tile Art Set")]
    public sealed class TileArtSet : ScriptableObject
    {
        [Header("Body — 2:1, transparent, thickness edge along the bottom")]
        [Tooltip("Wide tile: used for landscape hands, doubles, and bridges.")]
        public Sprite? BodyLandscape;

        [Tooltip("Tall tile: used for portrait hands and regular chain tiles.")]
        public Sprite? BodyPortrait;

        [Header("Face")]
        [Tooltip("A single pip. Placed by the existing pip-position table.")]
        public Sprite? Pip;

        [Tooltip("The centre bar with its node. Rotated for portrait tiles.")]
        public Sprite? Divider;

        /// <summary>
        /// The body for a given orientation, or null to draw procedurally.
        /// </summary>
        public Sprite? BodyFor(TileOrientation orientation) =>
            orientation == TileOrientation.Portrait ? BodyPortrait : BodyLandscape;
    }
}
