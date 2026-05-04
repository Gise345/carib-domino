#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// A player's intended action for their turn. Concrete subtypes are
    /// <see cref="PlaceMove"/> (lay a tile at one end of the chain) and
    /// <see cref="PassMove"/> (declare unable to play).
    /// </summary>
    public abstract class Move
    {
        public PlayerId Player { get; }

        protected Move(PlayerId player)
        {
            Player = player;
        }
    }
}
