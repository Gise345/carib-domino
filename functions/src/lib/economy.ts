/**
 * The coin economy — pure, server-authoritative money math (M6). No Firestore,
 * no side effects: every rule that decides how coins move lives here so it can be
 * unit-tested in isolation and reused by the wallet/roster callables. See ADR
 * 0016 and the economy design note.
 *
 * Model: a flat entry stake per player forms the pot; the match winner takes the
 * whole pot; a key (both-ends lock-out) earned during the series adds a minted
 * bonus on top. Losers simply forfeit their stake (it is already in the pot).
 */

/** Coins granted to a brand-new wallet. */
export const STARTING_COINS = 10_000;

/** Flat coins each player stakes to enter a match (Ludo-style tiers come later). */
export const ENTRY_STAKE = 1_000;

/** Minted bonus added to the winner's payout per key scored in the series. */
export const KEY_BONUS = 2_000;

/**
 * The pot for a table: every seated player stakes {@link ENTRY_STAKE}.
 * @param playerCount - seats staking into this match (2..4)
 * @returns the total pot in coins
 */
export function potFor(playerCount: number): number {
  if (!Number.isInteger(playerCount) || playerCount < 2 || playerCount > 4) {
    throw new RangeError(`playerCount must be an integer in 2..4, got ${String(playerCount)}.`);
  }
  return ENTRY_STAKE * playerCount;
}

/**
 * Splits a winner payout evenly across the winning side's members. Cut-Throat has
 * a single winner (the whole pot); Partner splits between the two partners. Any
 * indivisible remainder goes to the first winner so no coin is minted or lost.
 *
 * @param pot - the staked pot to distribute
 * @param keyCount - keys the winning side scored across the series (minted bonus)
 * @param winnerCount - number of uids sharing the win (1 solo, 2 partners)
 * @returns coins for each winner, index 0 carrying any remainder
 */
export function splitPayout(pot: number, keyCount: number, winnerCount: number): number[] {
  if (!Number.isInteger(winnerCount) || winnerCount < 1) {
    throw new RangeError(`winnerCount must be a positive integer, got ${String(winnerCount)}.`);
  }
  if (pot < 0 || keyCount < 0) {
    throw new RangeError('pot and keyCount must be non-negative.');
  }
  const total = pot + keyCount * KEY_BONUS;
  const base = Math.floor(total / winnerCount);
  const remainder = total - base * winnerCount;
  const shares = new Array<number>(winnerCount).fill(base);
  shares[0] = base + remainder;
  return shares;
}

/** Whether a wallet can cover the entry stake. */
export function canAfford(balance: number, stake: number = ENTRY_STAKE): boolean {
  return balance >= stake;
}
