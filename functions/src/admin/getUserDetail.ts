import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, DocumentData } from 'firebase-admin/firestore';
import { getAuth } from 'firebase-admin/auth';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';

if (getApps().length === 0) {
  initializeApp();
}

const DetailSchema = z.object({ uid: z.string().min(1).max(128) });

function num(data: DocumentData | undefined, field: string): number {
  const v: unknown = data?.[field];
  return typeof v === 'number' ? v : 0;
}

function str(data: DocumentData | undefined, field: string): string {
  const v: unknown = data?.[field];
  return typeof v === 'string' ? v : '';
}

/**
 * Full admin view of one player (ADR 0022, phase C): profile, auth (email +
 * linked providers + disabled), wallet balance, lifetime stats, and ban status —
 * one call for the Users detail panel. Admin-gated, read-only.
 *
 * @returns the player's aggregated admin detail.
 */
export const getUserDetail = onCall(async (request: CallableRequest<unknown>) => {
  assertAdmin(request);
  const parsed = DetailSchema.safeParse(request.data);
  if (!parsed.success) {
    throw new HttpsError('invalid-argument', 'Invalid uid.');
  }
  const uid = parsed.data.uid;
  const db = getFirestore();

  const [userSnap, statsSnap, walletSnap, banSnap] = await Promise.all([
    db.collection('users').doc(uid).get(),
    db.collection('stats').doc(uid).get(),
    db.collection('wallets').doc(uid).get(),
    db.collection('bans').doc(uid).get(),
  ]);

  let email = '';
  let providers: string[] = [];
  let disabled = false;
  try {
    const record = await getAuth().getUser(uid);
    email = record.email ?? '';
    providers = record.providerData.map((p) => p.providerId);
    disabled = record.disabled;
  } catch {
    // No Auth record for this uid — return Firestore data only.
  }

  const user = userSnap.data();
  const stats = statsSnap.data();
  const played = num(stats, 'matchesPlayed');
  const wins = num(stats, 'wins');

  return {
    uid,
    name: str(user, 'displayName') || 'Player',
    photoURL: str(user, 'photoURL'),
    email,
    providers,
    disabled,
    coins: num(walletSnap.data(), 'coins'),
    matchesPlayed: played,
    wins,
    losses: num(stats, 'losses'),
    draws: num(stats, 'draws'),
    totalScore: num(stats, 'totalScore'),
    winRate: played > 0 ? wins / played : 0,
    banned: banSnap.exists,
    banReason: str(banSnap.data(), 'reason'),
  };
});
