import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, DocumentData } from 'firebase-admin/firestore';
import { z } from 'zod';

if (getApps().length === 0) {
  initializeApp();
}

const LeaderboardSchema = z.object({
  scope: z.enum(['global', 'friends']).default('global'),
  metric: z.enum(['wins', 'points']).default('wins'),
  limit: z.number().int().min(1).max(100).default(50),
  /** App uids of the caller's Facebook friends (resolved client-side, M7). */
  friendUids: z.array(z.string().min(1)).max(500).optional(),
});

/** One ranked row. */
interface LeaderRow {
  uid: string;
  name: string;
  photoURL: string;
  wins: number;
  points: number;
  matchesPlayed: number;
  rank: number;
  isSelf: boolean;
}

const STAT_FIELD: Record<'wins' | 'points', string> = { wins: 'wins', points: 'totalScore' };

function num(data: DocumentData | undefined, field: string): number {
  const v: unknown = data?.[field];
  return typeof v === 'number' ? v : 0;
}

/**
 * Returns a ranked leaderboard (M7). `global` orders every player's stats by the
 * chosen metric; `friends` ranks only the caller + the supplied Facebook-friend
 * uids. Reads `/stats` (via the Admin SDK, so read-own client rules are bypassed
 * for this aggregate) and joins `/users` for display names. See ADR 0017.
 *
 * @returns `{ rows, metric, scope }` — rows already ranked, most-first.
 */
export const getLeaderboard = onCall(
  async (
    request: CallableRequest<unknown>,
  ): Promise<{ rows: LeaderRow[]; metric: string; scope: string }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to read the leaderboard.');
    }
    const parsed = LeaderboardSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError(
        'invalid-argument',
        `Invalid leaderboard query: ${parsed.error.message}`,
      );
    }
    const { scope, metric, limit, friendUids } = parsed.data;
    const self = request.auth.uid;
    const field = STAT_FIELD[metric];
    const db = getFirestore();

    // Gather (uid, statsData) pairs for the requested scope.
    let entries: { uid: string; data: DocumentData | undefined }[] = [];
    if (scope === 'friends') {
      const uids = Array.from(new Set([self, ...(friendUids ?? [])]));
      const snaps = await db.getAll(...uids.map((u) => db.collection('stats').doc(u)));
      entries = snaps.map((s) => ({ uid: s.id, data: s.data() }));
    } else {
      const q = await db.collection('stats').orderBy(field, 'desc').limit(limit).get();
      entries = q.docs.map((d) => ({ uid: d.id, data: d.data() }));
    }

    entries.sort((a, b) => num(b.data, field) - num(a.data, field));
    const top = entries.slice(0, limit);

    // Join display names + avatars.
    const names = new Map<string, string>();
    const photos = new Map<string, string>();
    if (top.length > 0) {
      const userSnaps = await db.getAll(...top.map((e) => db.collection('users').doc(e.uid)));
      for (const s of userSnaps) {
        const data = s.data();
        const dn: unknown = data?.['displayName'];
        const photo: unknown = data?.['photoURL'];
        names.set(s.id, typeof dn === 'string' && dn.length > 0 ? dn : 'Player');
        photos.set(s.id, typeof photo === 'string' ? photo : '');
      }
    }

    const rows: LeaderRow[] = top.map((e, i) => ({
      uid: e.uid,
      name: names.get(e.uid) ?? 'Player',
      photoURL: photos.get(e.uid) ?? '',
      wins: num(e.data, 'wins'),
      points: num(e.data, 'totalScore'),
      matchesPlayed: num(e.data, 'matchesPlayed'),
      rank: i + 1,
      isSelf: e.uid === self,
    }));

    return { rows, metric, scope };
  },
);
