import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, Transaction } from 'firebase-admin/firestore';
import { readOrInitWallet } from './wallet';

if (getApps().length === 0) {
  initializeApp();
}

/**
 * Returns the caller's coin balance (M6), creating and funding the wallet with
 * the starting balance on first access. Read-your-own only — the uid comes from
 * the auth context, never the payload, so a client can't read another wallet.
 * Wallets are otherwise client-readable directly (rules allow read-own); this
 * callable exists so a brand-new player's wallet is materialised on demand.
 *
 * @returns `{ coins }`
 */
export const getWallet = onCall(
  async (request: CallableRequest<unknown>): Promise<{ coins: number }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to read a wallet.');
    }
    const uid = request.auth.uid;
    const db = getFirestore();
    const coins = await db.runTransaction((txn: Transaction) => readOrInitWallet(db, txn, uid));
    return { coins };
  },
);
