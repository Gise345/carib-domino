#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// A tile placed in the chain, captured with explicit pip orientation.
    /// <see cref="LeftPip"/> is the pip facing the left side of the chain;
    /// <see cref="RightPip"/> is the pip facing the right side. For the very first
    /// tile placed on an empty chain, <see cref="LeftPip"/> equals <see cref="Tile.A"/>
    /// and <see cref="RightPip"/> equals <see cref="Tile.B"/>.
    /// </summary>
    public readonly struct PlacedTile
    {
        public Tile Tile { get; }
        public byte LeftPip { get; }
        public byte RightPip { get; }

        public PlacedTile(Tile tile, byte leftPip, byte rightPip)
        {
            Tile = tile;
            LeftPip = leftPip;
            RightPip = rightPip;
        }

        public override string ToString() => $"{LeftPip}|{RightPip} ({Tile})";
    }
}
