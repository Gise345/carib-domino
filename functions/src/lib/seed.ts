import { randomBytes } from 'node:crypto';

const UINT64_MAX = (1n << 64n) - 1n;

/**
 * Generates a cryptographically-random 64-bit seed as a decimal string. Returned
 * as a string because a 64-bit value exceeds JS's safe integer range and must
 * survive the round-trip to the client, which parses it into a `ulong` for the
 * deterministic deal (see the C#/TS `SeededRandomSource`).
 *
 * This is the anti-cheat core of the server-issued seed: because the client
 * never chooses the seed, it cannot search for one that deals it a loaded hand
 * (ADR 0007). Zero is excluded so a non-zero value always signals "issued".
 *
 * @returns A decimal string for an integer in [1, 2^64 - 1].
 */
export function generateSeed(): string {
  let value = 0n;
  while (value === 0n) {
    value = BigInt(`0x${randomBytes(8).toString('hex')}`);
  }
  return value.toString(10);
}

/** True if `s` is a decimal string for a value in [1, 2^64 - 1]. */
export function isValidSeedString(s: string): boolean {
  if (!/^[0-9]+$/.test(s)) {
    return false;
  }
  const v = BigInt(s);
  return v > 0n && v <= UINT64_MAX;
}
