import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue, Transaction } from 'firebase-admin/firestore';
import { facebookIdFromToken, facebookProfileFromToken } from './facebookIdentity';

if (getApps().length === 0) {
  initializeApp();
}

/**
 * Records the caller's verified Facebook identity so their friends can find them
 * (M7). After a client links Facebook onto its Firebase account it calls this;
 * the server reads the Facebook user id from the *signed auth token* (never the
 * payload) and writes two things in one transaction:
 *
 *   - `/facebookIndex/{fbId} -> { uid }` — the server-only fbId -> uid map that
 *     `resolveFacebookFriends` (M7 phase 2) uses to turn a friend's Facebook id
 *     into an app player. Client-unreadable, so the graph can't be scraped.
 *   - `/users/{uid}` display name + photo, mirrored from the Facebook profile so
 *     leaderboards and the profile card show a real name/avatar.
 *
 * If the Facebook id is already indexed to a *different* uid the account belongs
 * to another player, so we reject rather than hijack it — the client should have
 * signed into that existing account instead of linking. See ADR 0019.
 *
 * @returns `{ synced: true, facebookId }` on success.
 */
export const syncFacebookIdentity = onCall(
  async (request: CallableRequest<unknown>): Promise<{ synced: boolean; facebookId: string }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to sync a Facebook identity.');
    }
    const uid = request.auth.uid;
    const facebookId = facebookIdFromToken(request.auth.token);
    if (facebookId === null) {
      throw new HttpsError(
        'failed-precondition',
        'No Facebook identity is linked to this account.',
      );
    }
    const profile = facebookProfileFromToken(request.auth.token);

    const db = getFirestore();
    const indexRef = db.collection('facebookIndex').doc(facebookId);
    const userRef = db.collection('users').doc(uid);

    await db.runTransaction(async (txn: Transaction) => {
      const idxSnap = await txn.get(indexRef);
      const owner: unknown = idxSnap.data()?.['uid'];
      if (typeof owner === 'string' && owner !== uid) {
        throw new HttpsError(
          'already-exists',
          'This Facebook account is already linked to another player.',
        );
      }
      txn.set(indexRef, { uid, updatedAt: FieldValue.serverTimestamp() }, { merge: true });
      txn.set(userRef, { ...profile, updatedAt: FieldValue.serverTimestamp() }, { merge: true });
    });

    logger.info('syncFacebookIdentity', { uid, facebookId });
    return { synced: true, facebookId };
  },
);
