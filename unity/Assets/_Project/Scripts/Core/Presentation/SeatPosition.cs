#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// One of the four fixed seats around the table, as seen by the local
    /// player. The local player always occupies <see cref="Bottom"/>; the others
    /// are placed relative to them in turn order.
    /// </summary>
    public enum SeatPosition
    {
        Bottom,
        Right,
        Top,
        Left,
    }
}
