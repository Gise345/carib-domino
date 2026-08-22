import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, DocumentData, Transaction } from 'firebase-admin/firestore';
import { readOrInitWallet } from '../wallet/wallet';

if (getApps().length === 0) {
  initializeApp();
}

function num(data: DocumentData | undefined, field: string): number {
  const v: unknown = data?.[field];
  return typeof v === 'number' ? v : 0;
}

/**
 * Aggregates everything the player-profile card shows (M7): display name, coin
 * balance, and lifetime stats — read from `/users`, `/wallets` and `/stats` in
 * one call so the client makes a single request. Read-your-own only (the uid
 * comes from auth, never the payload). See ADR 0017.
 *
 * @returns the caller's profile card fields, with a derived win rate (0..1).
 */
export const getProfile = onCall(async (request: CallableRequest<unknown>) => {
  if (!request.auth?.uid) {
    throw new HttpsError('unauthenticated', 'Sign-in required to read a profile.');
  }
  const uid = request.auth.uid;
  const db = getFirestore();

  const snaps = await db.getAll(db.collection('users').doc(uid), db.collection('stats').doc(uid));
  const coins = await db.runTransaction((txn: Transaction) => readOrInitWallet(db, txn, uid));

  const user = snaps[0]?.data();
  const stats = snaps[1]?.data();
  const displayName: unknown = user?.['displayName'];
  const photoURL: unknown = user?.['photoURL'];
  const wins = num(stats, 'wins');
  const losses = num(stats, 'losses');
  const draws = num(stats, 'draws');
  const played = num(stats, 'matchesPlayed');

  return {
    uid,
    name: typeof displayName === 'string' && displayName.length > 0 ? displayName : 'Player',
    photoURL: typeof photoURL === 'string' ? photoURL : '',
    coins,
    matchesPlayed: played,
    wins,
    losses,
    draws,
    totalScore: num(stats, 'totalScore'),
    winRate: played > 0 ? wins / played : 0,
  };
});
