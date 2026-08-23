import { describe, expect, it } from 'vitest';
import { evaluateRedemption, normalizeCode, PromoState } from '../../src/admin/promotions';

const base: PromoState = {
  active: true,
  coins: 100,
  expiresAtMs: 0,
  maxRedemptions: 0,
  redemptionCount: 0,
};
const NOW = 1_000_000;

describe('normalizeCode', () => {
  it('uppercases and trims a valid code', () => {
    expect(normalizeCode('  welcome100 ')).toBe('WELCOME100');
  });

  it('rejects invalid codes', () => {
    expect(normalizeCode('ab')).toBeNull(); // too short
    expect(normalizeCode('has space')).toBeNull();
    expect(normalizeCode('bad-dash')).toBeNull();
    expect(normalizeCode('')).toBeNull();
  });
});

describe('evaluateRedemption', () => {
  it('awards coins when active, unexpired, uncapped, and unredeemed', () => {
    const r = evaluateRedemption(base, NOW, false);
    expect(r.ok).toBe(true);
    expect(r.coins).toBe(100);
  });

  it('blocks an inactive code', () => {
    expect(evaluateRedemption({ ...base, active: false }, NOW, false).ok).toBe(false);
  });

  it('blocks an expired code but allows before expiry', () => {
    expect(evaluateRedemption({ ...base, expiresAtMs: NOW - 1 }, NOW, false).ok).toBe(false);
    expect(evaluateRedemption({ ...base, expiresAtMs: NOW + 1 }, NOW, false).ok).toBe(true);
  });

  it('blocks a second redemption by the same player', () => {
    expect(evaluateRedemption(base, NOW, true).ok).toBe(false);
  });

  it('blocks once the redemption cap is reached', () => {
    expect(evaluateRedemption({ ...base, maxRedemptions: 5, redemptionCount: 5 }, NOW, false).ok).toBe(false);
    expect(evaluateRedemption({ ...base, maxRedemptions: 5, redemptionCount: 4 }, NOW, false).ok).toBe(true);
  });
});
