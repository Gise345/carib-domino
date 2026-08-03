import { describe, expect, it } from 'vitest';
import { generateSeed, isValidSeedString } from '../../src/lib/seed';

const UINT64_MAX = (1n << 64n) - 1n;

describe('generateSeed', () => {
  it('produces a valid, non-zero 64-bit decimal string', () => {
    for (let i = 0; i < 1000; i++) {
      const seed = generateSeed();
      expect(isValidSeedString(seed)).toBe(true);
      const v = BigInt(seed);
      expect(v).toBeGreaterThan(0n);
      expect(v).toBeLessThanOrEqual(UINT64_MAX);
    }
  });

  it('does not repeat across many draws (astronomically unlikely to collide)', () => {
    const seeds = new Set<string>();
    for (let i = 0; i < 1000; i++) {
      seeds.add(generateSeed());
    }
    expect(seeds.size).toBe(1000);
  });
});

describe('isValidSeedString', () => {
  it('accepts the boundary values', () => {
    expect(isValidSeedString('1')).toBe(true);
    expect(isValidSeedString(UINT64_MAX.toString(10))).toBe(true);
  });

  it('rejects zero, overflow, and non-numeric strings', () => {
    expect(isValidSeedString('0')).toBe(false);
    expect(isValidSeedString((UINT64_MAX + 1n).toString(10))).toBe(false);
    expect(isValidSeedString('')).toBe(false);
    expect(isValidSeedString('-1')).toBe(false);
    expect(isValidSeedString('12.3')).toBe(false);
    expect(isValidSeedString('abc')).toBe(false);
  });
});
