import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { SeededRandomSource } from '../../src/rules/prng';

interface PrngFixture {
  seed: string;
  uint64: string[];
  ints: { bound: number; value: number }[];
}

const here = dirname(fileURLToPath(import.meta.url));
const fixtures = JSON.parse(readFileSync(join(here, '../fixtures/prng-fixtures.json'), 'utf8')) as {
  prng: PrngFixture[];
};

describe('SeededRandomSource parity with C# SplitMix64', () => {
  it('has fixtures to check', () => {
    expect(fixtures.prng.length).toBeGreaterThan(0);
  });

  for (const fx of fixtures.prng) {
    it(`nextUInt64 sequence matches for seed ${fx.seed}`, () => {
      const src = new SeededRandomSource(BigInt(fx.seed));
      for (const expected of fx.uint64) {
        expect(src.nextUInt64()).toBe(BigInt(expected));
      }
    });

    it(`nextInt sequence matches for seed ${fx.seed}`, () => {
      const src = new SeededRandomSource(BigInt(fx.seed));
      for (const { bound, value } of fx.ints) {
        expect(src.nextInt(bound)).toBe(value);
      }
    });
  }
});
