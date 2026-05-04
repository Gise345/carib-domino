#nullable enable

namespace Pose.Core
{
    /// <summary>
    /// Abstraction over deterministic randomness for gameplay-affecting operations
    /// (tile shuffles, anything that influences match outcome). Production code never
    /// uses <c>System.Random</c>, <c>UnityEngine.Random</c>, or any other ambient
    /// RNG for these operations — all such randomness flows through this interface
    /// and is seeded from a server-issued value (see <c>docs/ARCHITECTURE.md</c>
    /// section 4, trust boundary 1).
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>
        /// Returns the next 64-bit unsigned value in the sequence.
        /// </summary>
        ulong NextUInt64();

        /// <summary>
        /// Returns a uniformly distributed non-negative integer strictly less than
        /// <paramref name="exclusiveUpperBound"/>. Implementations must use unbiased
        /// rejection sampling (modulo bias is not acceptable for gameplay RNG).
        /// </summary>
        int NextInt(int exclusiveUpperBound);
    }
}
