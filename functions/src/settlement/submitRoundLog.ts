import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue, Transaction } from 'firebase-admin/firestore';
import { z } from 'zod';
import { replayRound, ReplayMove } from '../rules';
import { resultForSeat } from './roundResult';

if (getApps().length === 0) {
  initializeApp();
}

const MoveSchema = z.object({
  playerIndex: z.number().int().min(0).max(3),
  kind: z.enum(['place', 'pass', 'resign']),
  low: z.number().int().min(0).max(6).optional(),
  high: z.number().int().min(0).max(6).optional(),
  end: z.enum(['left', 'right']).optional(),
});

/**
 * Payload for `submitRoundLog`. The host submits the finished round: the match
 * id it was seeded under, the players in seat order, each seat's Firebase uid
 * (`''` for a seat with no known uid — skipped), and the move log. The server
 * supplies neither the seed nor the outcome — it recomputes both.
 */
const SubmitRoundLogSchema = z
  .object({
    matchId: z.string().min(1),
    players: z.array(z.string().min(1)).min(2).max(4),
    seatUids: z.array(z.string()).min(2).max(4),
    moves: z.array(MoveSchema).min(1).max(200),
  })
  .refine((v) => v.players.length === v.seatUids.length, {
    message: 'players and seatUids must be the same length.',
  });

/**
 * Settles a finished online round by REPLAYING it, not by trusting a claimed
 * result (ADR 0007). The server:
 *   1. loads the seed it issued for `matchId` (rejects unknown / already-settled);
 *   2. confirms the caller is that match's host (the only party bound to the
 *      match server-side today — see the trust-gap note in ADR 0007);
 *   3. replays seed + move log through the canonical engine — rejecting any log
 *      that is illegal, out of turn, or doesn't finish the round;
 *   4. writes each seat's recomputed result to that seat's uid;
 *   5. marks the match settled, so a resubmit is a no-op (idempotent).
 *
 * All writes happen in one transaction with the settled check, so a race can't
 * double-count.
 *
 * @returns `{ ok: true, settled: boolean }` — `settled:false` means it was
 * already settled by an earlier call.
 */
export const submitRoundLog = onCall(
  async (request: CallableRequest<unknown>): Promise<{ ok: true; settled: boolean }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to submit a round.');
    }

    const parsed = SubmitRoundLogSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', `Invalid round log: ${parsed.error.message}`);
    }
    const { matchId, players, seatUids, moves } = parsed.data;
    const uid = request.auth.uid;

    const db = getFirestore();
    const matchRef = db.collection('matches').doc(matchId);

    const settled = await db.runTransaction(async (txn: Transaction): Promise<boolean> => {
      const snap = await txn.get(matchRef);
      if (!snap.exists) {
        throw new HttpsError('not-found', `Unknown match ${matchId}.`);
      }
      const data = snap.data() ?? {};

      if (data['hostUid'] !== uid) {
        throw new HttpsError('permission-denied', 'Only the match host can submit its result.');
      }
      if (data['settled'] === true) {
        return false; // already counted — idempotent no-op
      }

      const seed: unknown = data['seed'];
      if (typeof seed !== 'string') {
        throw new HttpsError('failed-precondition', 'Match has no recorded seed.');
      }

      // Recompute the authoritative outcome. A bad log means the submission
      // could not have happened — reject it rather than write anything.
      const outcome = replay(seed, players, moves);

      for (let i = 0; i < players.length; i++) {
        const seatUid = seatUids[i];
        if (seatUid === undefined || seatUid === '') {
          continue;
        }
        const { result, score } = resultForSeat(outcome, players, i);
        txn.set(db.collection('stats').doc(seatUid), buildStatsUpdate(result, score), {
          merge: true,
        });
      }

      txn.update(matchRef, { settled: true, settledAt: FieldValue.serverTimestamp() });
      return true;
    });

    logger.info('submitRoundLog processed', { matchId, hostUid: uid, settled });
    return { ok: true, settled };
  },
);

function replay(seed: string, players: string[], moves: ReplayMove[]) {
  try {
    return replayRound({ seed, players, moves });
  } catch (e) {
    const message = e instanceof Error ? e.message : 'replay failed';
    throw new HttpsError('invalid-argument', `Round log did not validate: ${message}`);
  }
}

function buildStatsUpdate(result: 'won' | 'lost' | 'draw', score: number): Record<string, unknown> {
  const update: Record<string, unknown> = {
    matchesPlayed: FieldValue.increment(1),
    lastMatchAt: FieldValue.serverTimestamp(),
    lastResult: result,
  };
  switch (result) {
    case 'won':
      update['wins'] = FieldValue.increment(1);
      update['totalScore'] = FieldValue.increment(score);
      break;
    case 'lost':
      update['losses'] = FieldValue.increment(1);
      break;
    case 'draw':
      update['draws'] = FieldValue.increment(1);
      break;
  }
  return update;
}
