#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// A move where a player concedes the round (online resign button or
    /// disconnect). Ends the round immediately; the non-resigner(s) win and
    /// score the sum of the resigner's remaining pips.
    /// </summary>
    public sealed class ResignMove : Move
    {
        public ResignMove(PlayerId player)
            : base(player)
        {
        }

        public override string ToString() => $"{Player} RESIGN";
    }
}
