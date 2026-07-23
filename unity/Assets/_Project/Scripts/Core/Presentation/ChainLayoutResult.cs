#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// The full result of laying out a chain: one <see cref="ChainSlot"/> per
    /// tile (parallel to the source chain's tile order) plus the centers of the
    /// two end drop-zones, all in the same logical layout units as
    /// <see cref="ChainSlot"/>.
    /// </summary>
    public readonly struct ChainLayoutResult
    {
        public readonly ChainSlot[] Slots;
        public readonly float LeftZoneX;
        public readonly float LeftZoneY;
        public readonly float RightZoneX;
        public readonly float RightZoneY;

        public ChainLayoutResult(
            ChainSlot[] slots,
            float leftZoneX,
            float leftZoneY,
            float rightZoneX,
            float rightZoneY)
        {
            Slots = slots;
            LeftZoneX = leftZoneX;
            LeftZoneY = leftZoneY;
            RightZoneX = rightZoneX;
            RightZoneY = rightZoneY;
        }
    }
}
