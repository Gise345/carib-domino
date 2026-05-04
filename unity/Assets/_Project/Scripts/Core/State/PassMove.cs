#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// A move where a player declares they cannot play any tile this turn. Only legal
    /// in Block-style rules when the player truly has no tile matching either chain end.
    /// </summary>
    public sealed class PassMove : Move
    {
        public PassMove(PlayerId player)
            : base(player)
        {
        }

        public override string ToString() => $"{Player} PASS";
    }
}
