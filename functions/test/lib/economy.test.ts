import { describe, expect, it } from 'vitest';
import {
  ENTRY_STAKE,
  KEY_BONUS,
  STARTING_COINS,
  canAfford,
  potFor,
  splitPayout,
} from '../../src/lib/economy';

describe('economy constants', () => {
  it('matches the agreed design (10k start, 1k entry, 2k key bonus)', () => {
    expect(STARTING_COINS).toBe(10_000);
    expect(ENTRY_STAKE).toBe(1_000);
    expect(KEY_BONUS).toBe(2_000);
  });
});

describe('potFor', () => {
  it('is the stake times the seat count', () => {
    expect(potFor(2)).toBe(2_000);
    expect(potFor(3)).toBe(3_000);
    expect(potFor(4)).toBe(4_000);
  });

  it('rejects out-of-range or non-integer seat counts', () => {
    expect(() => potFor(1)).toThrow(RangeError);
    expect(() => potFor(5)).toThrow(RangeError);
    expect(() => potFor(2.5)).toThrow(RangeError);
  });
});

describe('splitPayout', () => {
  it('gives a solo winner the whole pot with no key', () => {
    expect(splitPayout(4_000, 0, 1)).toEqual([4_000]);
  });

  it('adds the minted key bonus per key scored', () => {
    expect(splitPayout(4_000, 1, 1)).toEqual([4_000 + KEY_BONUS]);
    expect(splitPayout(3_000, 2, 1)).toEqual([3_000 + 2 * KEY_BONUS]);
  });

  it('splits a Partner pot evenly between two winners', () => {
    expect(splitPayout(4_000, 0, 2)).toEqual([2_000, 2_000]);
  });

  it('conserves coins: an odd total puts the remainder on the first winner', () => {
    const shares = splitPayout(3_000, 0, 2); // 1500 each, exact
    expect(shares).toEqual([1_500, 1_500]);

    const odd = splitPayout(3_000, 1, 2); // (3000 + 2000) / 2 = 2500 each
    expect(odd).toEqual([2_500, 2_500]);
    expect(odd[0]! + odd[1]!).toBe(5_000);

    const remainder = splitPayout(5_000, 0, 2); // 2500 each
    expect(remainder.reduce((a, b) => a + b, 0)).toBe(5_000);

    const three = splitPayout(4_000, 0, 3); // 1333 + remainder
    expect(three.reduce((a, b) => a + b, 0)).toBe(4_000);
    expect(three[0]).toBe(1_334);
  });

  it('rejects bad arguments', () => {
    expect(() => splitPayout(1_000, 0, 0)).toThrow(RangeError);
    expect(() => splitPayout(-1, 0, 1)).toThrow(RangeError);
    expect(() => splitPayout(1_000, -1, 1)).toThrow(RangeError);
  });
});

describe('canAfford', () => {
  it('needs the balance to cover the stake', () => {
    expect(canAfford(1_000)).toBe(true);
    expect(canAfford(999)).toBe(false);
    expect(canAfford(5_000, 3_000)).toBe(true);
  });
});
