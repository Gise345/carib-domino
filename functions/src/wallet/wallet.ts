import { FieldValue, Firestore, Transaction } from 'firebase-admin/firestore';
import { STARTING_COINS } from '../lib/economy';

/**
 * Server-authoritative coin wallet access (M6). Wallets live at
 * `wallets/{uid}`; Firestore rules make them client-read-own / no-client-write,
 * so every mutation goes through these Cloud-Functions helpers inside a
 * transaction. A wallet is created lazily the first time it is touched, funded
 * with {@link STARTING_COINS}. See ADR 0016.
 */

/** A player's wallet document shape. */
export interface Wallet {
  readonly coins: number;
}

/** The `wallets` collection path for a uid. */
export function walletRef(db: Firestore, uid: string) {
  return db.collection('wallets').doc(uid);
}

/**
 * Reads a wallet inside a transaction, creating it with the starting balance if
 * it does not exist yet. Always returns a concrete balance.
 *
 * @param db - Firestore instance
 * @param txn - the enclosing transaction (all reads must precede writes)
 * @param uid - the player's Firebase uid
 * @returns the wallet's current coin balance
 */
export async function readOrInitWallet(
  db: Firestore,
  txn: Transaction,
  uid: string,
): Promise<number> {
  const ref = walletRef(db, uid);
  const snap = await txn.get(ref);
  if (!snap.exists) {
    txn.set(ref, { coins: STARTING_COINS, createdAt: FieldValue.serverTimestamp() });
    return STARTING_COINS;
  }
  const coins: unknown = snap.data()?.['coins'];
  return typeof coins === 'number' ? coins : STARTING_COINS;
}

/**
 * Debits `amount` coins from a uid inside a transaction. The caller must have
 * already read the balance via {@link readOrInitWallet} and confirmed it can
 * cover the debit — this only issues the decrement.
 */
export function debit(db: Firestore, txn: Transaction, uid: string, amount: number): void {
  if (amount < 0) {
    throw new Error('debit amount must be non-negative.');
  }
  txn.set(
    walletRef(db, uid),
    { coins: FieldValue.increment(-amount), updatedAt: FieldValue.serverTimestamp() },
    { merge: true },
  );
}

/** Credits `amount` coins to a uid inside a transaction. */
export function credit(db: Firestore, txn: Transaction, uid: string, amount: number): void {
  if (amount < 0) {
    throw new Error('credit amount must be non-negative.');
  }
  txn.set(
    walletRef(db, uid),
    { coins: FieldValue.increment(amount), updatedAt: FieldValue.serverTimestamp() },
    { merge: true },
  );
}
