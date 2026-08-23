/**
 * Per-sender chat rate limiting (ADR 0023). Pure: the decision is computed from
 * the sender's recent send times, so it is unit-testable and the callable only
 * has to persist the window.
 *
 * Two rules, both needed: a sliding-window allowance stops burst flooding, and a
 * minimum gap stops a hold-to-send macro from trickling out exactly at the
 * window edge forever.
 */

import { RATE_MAX_IN_WINDOW, RATE_MIN_GAP_MS, RATE_WINDOW_MS } from './model';

/** Whether a send is allowed, and the window to persist for next time. */
export interface RateDecision {
  /** True when the message may go through. */
  readonly allowed: boolean;
  /** Milliseconds until the sender may try again (0 when allowed). */
  readonly retryAfterMs: number;
  /** The timestamps to store — the trimmed window, plus `now` when allowed. */
  readonly window: number[];
}

/**
 * Decides whether a sender may post right now.
 *
 * @param recent - epoch-ms timestamps of the sender's previous messages
 * @param now - current epoch-ms
 * @returns the decision plus the window to persist
 */
export function evaluateRateLimit(recent: readonly number[], now: number): RateDecision {
  const window = recent.filter((t) => now - t < RATE_WINDOW_MS).sort((a, b) => a - b);

  const last = window.length > 0 ? window[window.length - 1] : undefined;
  if (last !== undefined && now - last < RATE_MIN_GAP_MS) {
    return { allowed: false, retryAfterMs: RATE_MIN_GAP_MS - (now - last), window };
  }

  if (window.length >= RATE_MAX_IN_WINDOW) {
    const [oldest = now] = window;
    return { allowed: false, retryAfterMs: RATE_WINDOW_MS - (now - oldest), window };
  }

  return { allowed: true, retryAfterMs: 0, window: [...window, now] };
}
