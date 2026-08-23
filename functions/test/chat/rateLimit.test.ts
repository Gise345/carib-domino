import { describe, expect, it } from 'vitest';
import { evaluateRateLimit } from '../../src/chat/rateLimit';
import {
  RATE_MAX_IN_WINDOW,
  RATE_MIN_GAP_MS,
  RATE_WINDOW_MS,
} from '../../src/chat/model';

const NOW = 1_700_000_000_000;

describe('evaluateRateLimit', () => {
  it('allows the first message', () => {
    const decision = evaluateRateLimit([], NOW);

    expect(decision.allowed).toBe(true);
    expect(decision.retryAfterMs).toBe(0);
    expect(decision.window).toEqual([NOW]);
  });

  it('records each allowed send in the window', () => {
    const decision = evaluateRateLimit([NOW - 5_000], NOW);

    expect(decision.allowed).toBe(true);
    expect(decision.window).toEqual([NOW - 5_000, NOW]);
  });

  it('rejects a send inside the minimum gap', () => {
    const decision = evaluateRateLimit([NOW - 100], NOW);

    expect(decision.allowed).toBe(false);
    expect(decision.retryAfterMs).toBe(RATE_MIN_GAP_MS - 100);
    expect(decision.window).toEqual([NOW - 100]); // the rejected send is not recorded
  });

  it('rejects once the window allowance is spent', () => {
    const spent = Array.from(
      { length: RATE_MAX_IN_WINDOW },
      (_, i) => NOW - 1_000 * (i + 1),
    );

    const decision = evaluateRateLimit(spent, NOW);

    expect(decision.allowed).toBe(false);
    expect(decision.retryAfterMs).toBeGreaterThan(0);
  });

  it('forgets sends that have aged out of the window', () => {
    const old = Array.from(
      { length: RATE_MAX_IN_WINDOW },
      (_, i) => NOW - RATE_WINDOW_MS - 1_000 * (i + 1),
    );

    const decision = evaluateRateLimit(old, NOW);

    expect(decision.allowed).toBe(true);
    expect(decision.window).toEqual([NOW]); // the aged entries are dropped, not kept
  });

  it('lets a burst through then holds the sender until the window rolls', () => {
    let window: number[] = [];
    let clock = NOW;

    // Send as fast as the gap allows.
    for (let i = 0; i < RATE_MAX_IN_WINDOW; i++) {
      const decision = evaluateRateLimit(window, clock);
      expect(decision.allowed).toBe(true);
      window = decision.window;
      clock += RATE_MIN_GAP_MS;
    }

    const blocked = evaluateRateLimit(window, clock);
    expect(blocked.allowed).toBe(false);

    const later = evaluateRateLimit(window, clock + RATE_WINDOW_MS);
    expect(later.allowed).toBe(true);
  });

  it('tolerates an unsorted stored window', () => {
    const decision = evaluateRateLimit([NOW - 2_000, NOW - 9_000, NOW - 5_000], NOW);

    expect(decision.allowed).toBe(true);
    expect(decision.window).toEqual([NOW - 9_000, NOW - 5_000, NOW - 2_000, NOW]);
  });
});
