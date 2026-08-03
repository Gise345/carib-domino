/**
 * Deterministic PRNG — SplitMix64. A byte-for-byte port of the C# client's
 * `Pose.Core.SeededRandomSource` (see
 * `unity/Assets/_Project/Scripts/Core/Utils/SeededRandomSource.cs`). The two
 * MUST produce identical sequences for the same seed: the whole settlement
 * trust model rests on the server reconstructing the exact deal the clients
 * played (ARCHITECTURE.md §5, ADR 0007). Not cryptographically secure — do not
 * use for anything security-sensitive.
 *
 * All arithmetic is done on 64-bit unsigned values via BigInt masked to 64 bits,
 * mirroring C#'s `unchecked` ulong overflow semantics.
 */

const MASK64 = (1n << 64n) - 1n;
const GOLDEN = 0x9e3779b97f4a7c15n;
const MIX1 = 0xbf58476d1ce4e5b9n;
const MIX2 = 0x94d049bb133111ebn;

/** Largest value a 64-bit unsigned integer can hold — C#'s `ulong.MaxValue`. */
const UINT64_MAX = MASK64;

export class SeededRandomSource {
  private state: bigint;

  /**
   * @param seed 64-bit unsigned seed. Values outside [0, 2^64) are wrapped to
   * 64 bits so a C# `ulong` handed over as a decimal string round-trips exactly.
   */
  constructor(seed: bigint) {
    this.state = seed & MASK64;
  }

  /** Returns the next 64-bit unsigned value in the sequence. */
  nextUInt64(): bigint {
    this.state = (this.state + GOLDEN) & MASK64;
    let z = this.state;
    z = ((z ^ (z >> 30n)) * MIX1) & MASK64;
    z = ((z ^ (z >> 27n)) * MIX2) & MASK64;
    return (z ^ (z >> 31n)) & MASK64;
  }

  /**
   * Returns a uniformly-distributed integer in [0, exclusiveUpperBound) using
   * the same unbiased rejection sampling as the C# source.
   *
   * @param exclusiveUpperBound Must be positive.
   * @returns An integer in [0, exclusiveUpperBound).
   */
  nextInt(exclusiveUpperBound: number): number {
    if (exclusiveUpperBound <= 0) {
      throw new RangeError('exclusiveUpperBound must be positive.');
    }
    const range = BigInt(exclusiveUpperBound);
    const limit = (UINT64_MAX / range) * range;
    let v: bigint;
    do {
      v = this.nextUInt64();
    } while (v >= limit);
    return Number(v % range);
  }
}
