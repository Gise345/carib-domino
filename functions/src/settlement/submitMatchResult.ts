import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';

if (getApps().length === 0) {
  initializeApp();
}

/**
 * Schema for the round-result payload the client submits.
 *
 * - `outcome` is from the *caller's* perspective: did the authenticated user
 *   win, lose, or tie this round?
 * - `endReason` is which engine code path the round ended on:
 *   `domino` (somebody emptied their hand) or `blocked` (everybody passed in
 *   succession). `draw` exists for the edge case in partner variants where the
 *   end-of-round logic resolves to a draw without being a block.
 * - `score` is the pip score earned by the winner; 0 for losses/draws.
 *
 * For M2.3 this function trusts these values — it doesn't yet have the match
 * log or seed to replay and verify. M4 introduces the replay validator that
 * recomputes the outcome from the seed + move log and rejects mismatches.
 */
const RoundResultSchema = z.object({
  outcome: z.enum(['won', 'lost', 'draw']),
  endReason: z.enum(['domino', 'blocked', 'draw']),
  score: z.number().int().min(0).max(168),
});

type RoundResult = z.infer<typeof RoundResultSchema>;

/**
 * Callable Cloud Function — the only path through which `/stats/{uid}` is
 * ever written. Firestore security rules deny all client writes to `/stats`,
 * which forces this server-side route. The function uses the Admin SDK
 * (bypasses rules) to do atomic increments on the caller's stats document.
 *
 * Per the trust model in `docs/ARCHITECTURE.md` §4, this is the foundational
 * shape for every wallet/ELO/stats write the project will introduce:
 *   1. Validate `request.auth` exists.
 *   2. Validate the input payload with zod.
 *   3. (M4) Replay the match log to verify the claimed outcome.
 *   4. Apply the side effects atomically.
 */
export const submitMatchResult = onCall(
  async (request: CallableRequest<unknown>): Promise<{ ok: true; uid: string }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to submit a match result.');
    }

    const parsed = RoundResultSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError(
        'invalid-argument',
        `Invalid match result payload: ${parsed.error.message}`,
      );
    }

    const uid = request.auth.uid;
    const result: RoundResult = parsed.data;

    const update: Record<string, unknown> = {
      matchesPlayed: FieldValue.increment(1),
      lastMatchAt: FieldValue.serverTimestamp(),
      lastResult: result.outcome,
      lastEndReason: result.endReason,
    };

    // Track wins, losses, and draws as separate counters per the user's
    // request — derived stats (e.g. win rate) can be computed downstream.
    // Bracket notation here because `update` is typed as Record<string, …>
    // and tsconfig's strict index access requires it for arbitrary keys.
    switch (result.outcome) {
      case 'won':
        update['wins'] = FieldValue.increment(1);
        update['totalScore'] = FieldValue.increment(result.score);
        break;
      case 'lost':
        update['losses'] = FieldValue.increment(1);
        break;
      case 'draw':
        update['draws'] = FieldValue.increment(1);
        break;
    }

    // merge:true so the doc creates on first match and partial updates work
    // on subsequent matches without overwriting unrelated fields.
    const db = getFirestore();
    await db.collection('stats').doc(uid).set(update, { merge: true });

    logger.info('submitMatchResult applied', {
      uid,
      outcome: result.outcome,
      endReason: result.endReason,
      score: result.score,
    });

    return { ok: true, uid };
  },
);
