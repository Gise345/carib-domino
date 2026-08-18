import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, DocumentData } from 'firebase-admin/firestore';
import { z } from 'zod';
import { resolveFriendUids } from './facebookFriends';

if (getApps().length === 0) {
  initializeApp();
}

const ResolveSchema = z.object({
  /** The caller's Facebook friend ids (from the FB graph `/me/friends`). */
  facebookIds: z.array(z.string().min(1).max(64)).max(1000),
});

/** One resolved app friend. */
interface Friend {
  uid: string;
  name: string;
  wins: number;
  matchesPlayed: number;
}

function nameOf(data: DocumentData | undefined): string {
  const dn: unknown = data?.['displayName'];
  return typeof dn === 'string' && dn.length > 0 ? dn : 'Player';
}

function num(data: DocumentData | undefined, field: string): number {
  const v: unknown = data?.[field];
  return typeof v === 'number' ? v : 0;
}

/**
 * Resolves the caller's Facebook friends who also play Pose (M7). The client
 * fetches friend ids from Facebook (only friends who granted `user_friends` are
 * returned) and passes them here; the server maps each id to an app uid through
 * the server-only `/facebookIndex`, then joins `/users` and `/stats` so the
 * friends list / friends leaderboard render in one call. The uids returned can be
 * fed straight into `getLeaderboard` with `scope: 'friends'`. See ADR 0019.
 *
 * @returns `{ friends }` — unique app friends (self excluded), each with name + record.
 */
export const resolveFacebookFriends = onCall(
  async (request: CallableRequest<unknown>): Promise<{ friends: Friend[] }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to resolve friends.');
    }
    const parsed = ResolveSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', `Invalid friends query: ${parsed.error.message}`);
    }
    const self = request.auth.uid;
    const { facebookIds } = parsed.data;
    if (facebookIds.length === 0) {
      return { friends: [] };
    }
    const db = getFirestore();

    // fbId -> uid via the server-only index.
    const idxSnaps = await db.getAll(
      ...facebookIds.map((id) => db.collection('facebookIndex').doc(id)),
    );
    const uidByFacebookId = new Map<string, string>();
    idxSnaps.forEach((snap, i) => {
      const fbId = facebookIds[i];
      if (fbId === undefined) {
        return;
      }
      const uid: unknown = snap.data()?.['uid'];
      if (typeof uid === 'string' && uid.length > 0) {
        uidByFacebookId.set(fbId, uid);
      }
    });

    const uids = resolveFriendUids(facebookIds, uidByFacebookId, self);
    if (uids.length === 0) {
      return { friends: [] };
    }

    // Join names + records for the resolved friends.
    const [userSnaps, statSnaps] = await Promise.all([
      db.getAll(...uids.map((u) => db.collection('users').doc(u))),
      db.getAll(...uids.map((u) => db.collection('stats').doc(u))),
    ]);

    const friends: Friend[] = uids.map((uid, i) => {
      const userData = userSnaps[i]?.data();
      const statData = statSnaps[i]?.data();
      return {
        uid,
        name: nameOf(userData),
        wins: num(statData, 'wins'),
        matchesPlayed: num(statData, 'matchesPlayed'),
      };
    });

    return { friends };
  },
);
