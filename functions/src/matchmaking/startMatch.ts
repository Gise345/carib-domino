import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';
import { generateSeed } from '../lib/seed';

if (getApps().length === 0) {
  initializeApp();
}

/**
 * Input for `startMatch`: just the table size. The seed is NOT accepted from the
 * client — that is the whole point (ADR 0007).
 */
const StartMatchSchema = z.object({
  playerCount: z.number().int().min(2).max(4),
});

/**
 * Callable that issues a server-generated seed for one round and records it, so
 * the client can never pick a favourable deal. Each call creates one
 * `matches/{matchId}` document (a single settleable round; a rematch calls again
 * for a fresh seed). M4.3's settlement function looks the seed up by `matchId`
 * and replays the submitted move log against it — the client can lie about
 * neither the seed nor the outcome.
 *
 * The seed doc is Cloud-Functions-only (Firestore rules deny all client access);
 * the seed reaches clients solely through this function's return value.
 *
 * @returns `{ matchId, seed }` — the seed as a decimal string for a `ulong`.
 */
export const startMatch = onCall(
  async (request: CallableRequest<unknown>): Promise<{ matchId: string; seed: string }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to start a match.');
    }

    const parsed = StartMatchSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError(
        'invalid-argument',
        `Invalid startMatch payload: ${parsed.error.message}`,
      );
    }

    const uid = request.auth.uid;
    const seed = generateSeed();

    const db = getFirestore();
    const ref = await db.collection('matches').add({
      seed,
      hostUid: uid,
      playerCount: parsed.data.playerCount,
      settled: false,
      createdAt: FieldValue.serverTimestamp(),
    });

    logger.info('startMatch issued seed', {
      matchId: ref.id,
      hostUid: uid,
      playerCount: parsed.data.playerCount,
    });

    return { matchId: ref.id, seed };
  },
);
