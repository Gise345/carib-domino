#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Deterministic PRNG using SplitMix64. Chosen for two reasons: it has
    /// well-defined behaviour with no platform variation, and the algorithm is
    /// trivial to mirror byte-for-byte in TypeScript on the server side (see
    /// <c>docs/ARCHITECTURE.md</c> section 7.2 — rule-engine parity). Not
    /// cryptographically secure; do not use for any security-sensitive purpose.
    /// </summary>
    public sealed class SeededRandomSource : IRandomSource
    {
        private ulong _state;

        public SeededRandomSource(ulong seed)
        {
            _state = seed;
        }

        public ulong NextUInt64()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveUpperBound),
                    "Must be positive.");
            }

            // Unbiased rejection sampling: accept only values in [0, limit) where
            // limit is the largest multiple of the range that fits in a ulong, so
            // (v % range) is uniformly distributed.
            ulong range = (ulong)exclusiveUpperBound;
            ulong limit = (ulong.MaxValue / range) * range;
            ulong v;
            do
            {
                v = NextUInt64();
            }
            while (v >= limit);
            return (int)(v % range);
        }
    }
}
