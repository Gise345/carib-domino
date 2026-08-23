/**
 * Pure promo-code logic (admin subsystem, ADR 0022 phase E). Side-effect free so
 * the code-format and redemption rules are unit-tested in isolation and reused by
 * the create / redeem callables.
 */

/** A promotion's redemption-relevant state, as stored in `/promotions/{code}`. */
export interface PromoState {
  readonly active: boolean;
  readonly coins: number;
  /** Epoch ms the code expires; 0 = never. */
  readonly expiresAtMs: number;
  /** Max total redemptions; 0 = unlimited. */
  readonly maxRedemptions: number;
  readonly redemptionCount: number;
}

/** The outcome of evaluating one redemption attempt. */
export interface RedemptionResult {
  readonly ok: boolean;
  readonly reason: string;
  readonly coins: number;
}

/**
 * Normalises a promo code to the canonical form (upper-case, trimmed) if it is
 * valid (3–32 letters/digits), else null.
 *
 * @param raw - the raw code as typed
 * @returns the normalised code, or null if invalid
 */
export function normalizeCode(raw: string): string | null {
  const code = raw.trim().toUpperCase();
  return /^[A-Z0-9]{3,32}$/.test(code) ? code : null;
}

/**
 * Decides whether a player may redeem a promo now.
 *
 * @param promo - the stored promotion state
 * @param nowMs - current time (epoch ms)
 * @param alreadyRedeemed - whether this player already redeemed this code
 * @returns ok + reason + the coins to award (0 unless ok)
 */
export function evaluateRedemption(
  promo: PromoState,
  nowMs: number,
  alreadyRedeemed: boolean,
): RedemptionResult {
  if (!promo.active) {
    return { ok: false, reason: 'This code is no longer active.', coins: 0 };
  }
  if (promo.expiresAtMs > 0 && nowMs >= promo.expiresAtMs) {
    return { ok: false, reason: 'This code has expired.', coins: 0 };
  }
  if (alreadyRedeemed) {
    return { ok: false, reason: 'You already redeemed this code.', coins: 0 };
  }
  if (promo.maxRedemptions > 0 && promo.redemptionCount >= promo.maxRedemptions) {
    return { ok: false, reason: 'This code has reached its redemption limit.', coins: 0 };
  }
  return { ok: true, reason: '', coins: promo.coins };
}
