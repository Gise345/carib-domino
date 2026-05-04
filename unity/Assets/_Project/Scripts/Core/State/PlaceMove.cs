#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// A move that places a tile from the player's hand at one end of the chain.
    /// The end is meaningless for the very first move on an empty chain (both ends
    /// are open) — <see cref="ChainEnd.Left"/> is conventionally used in that case.
    /// </summary>
    public sealed class PlaceMove : Move
    {
        public Tile Tile { get; }
        public ChainEnd End { get; }

        public PlaceMove(PlayerId player, Tile tile, ChainEnd end)
            : base(player)
        {
            Tile = tile;
            End = end;
        }

        public override string ToString() => $"{Player} PLACE {Tile} on {End}";
    }
}
