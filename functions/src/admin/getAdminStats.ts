import { onCall, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, Timestamp, AggregateField } from 'firebase-admin/firestore';
import { assertAdmin } from './requireAdmin';

if (getApps().length === 0) {
  initializeApp();
}

const ACTIVE_WINDOW_MS = 7 * 24 * 60 * 60 * 1000;

/**
 * Top-line game analytics for the admin dashboard (ADR 0022, phase C). Uses
 * Firestore aggregation queries (count / sum) so it scales without reading whole
 * collections. Admin-gated; read-only (no audit entry). See ADR 0022.
 *
 * @returns total users, 7-day active users, rounds played, and coins in circulation.
 */
export const getAdminStats = onCall(
  async (
    request: CallableRequest<unknown>,
  ): Promise<{
    totalUsers: number;
    activeUsers7d: number;
    rounds: number;
    coinsInCirculation: number;
  }> => {
    assertAdmin(request);
    const db = getFirestore();
    const cutoff = Timestamp.fromMillis(Date.now() - ACTIVE_WINDOW_MS);

    const [usersSnap, activeSnap, roundsSnap, coinsSnap] = await Promise.all([
      db.collection('users').count().get(),
      db.collection('users').where('lastSeenAt', '>=', cutoff).count().get(),
      db.collection('matches').count().get(),
      db
        .collection('wallets')
        .aggregate({ total: AggregateField.sum('coins') })
        .get(),
    ]);

    return {
      totalUsers: usersSnap.data().count,
      activeUsers7d: activeSnap.data().count,
      rounds: roundsSnap.data().count,
      coinsInCirculation: coinsSnap.data().total,
    };
  },
);
