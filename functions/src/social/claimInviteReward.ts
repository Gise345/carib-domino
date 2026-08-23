import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { assertNotBanned } from '../admin/bans';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue, Transaction } from 'firebase-admin/firestore';
import { z } from 'zod';
import { credit, readOrInitWallet } from '../wallet/wallet';
import { evaluateInvite, InviteDay } from './invite';

if (getApps().length === 0) {
  initializeApp();
}

const ClaimInviteSchema = z.object({
  /** Unique id for this invite (a client UUID, or the FB App-Request id). */
  inviteId: z.string().min(1).max(200),
});

/** The current UTC date as `YYYY-MM-DD`. */
function utcToday(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Rewards a player for sending a Facebook friend invite (M7). Server-authoritative
 * and abuse-resistant: {@link evaluateInvite} caps rewards to a few per UTC day and
 * de-dups by `inviteId`, and the wallet credit + record write happen in one
 * transaction so concurrent claims can't over-pay. The reward is minted (it is a
 * growth incentive, not moved from another wallet). See ADR 0017.
 *
 * @returns `{ rewarded, coins, remainingToday, outcome }`.
 */
export const claimInviteReward = onCall(
  async (
    request: CallableRequest<unknown>,
  ): Promise<{ rewarded: boolean; coins: number; remainingToday: number; outcome: string }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to claim an invite reward.');
    }
    const parsed = ClaimInviteSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', `Invalid claim: ${parsed.error.message}`);
    }
    const uid = request.auth.uid;
    await assertNotBanned(uid);
    const { inviteId } = parsed.data;
    const today = utcToday();

    const db = getFirestore();
    const ref = db.collection('inviteRewards').doc(uid);

    const result = await db.runTransaction(async (txn: Transaction) => {
      const snap = await txn.get(ref);
      const data = snap.data();
      const record: InviteDay | null =
        data && typeof data['day'] === 'string' && Array.isArray(data['ids'])
          ? { day: data['day'], ids: data['ids'] as string[] }
          : null;

      const evalResult = evaluateInvite(record, today, inviteId);

      // Read the wallet regardless (also materialises it) before any write.
      const balanceBefore = await readOrInitWallet(db, txn, uid);

      if (evalResult.reward > 0) {
        credit(db, txn, uid, evalResult.reward);
        txn.set(
          ref,
          {
            day: evalResult.nextRecord.day,
            ids: evalResult.nextRecord.ids,
            updatedAt: FieldValue.serverTimestamp(),
          },
          { merge: true },
        );
      }

      return {
        rewarded: evalResult.reward > 0,
        coins: balanceBefore + evalResult.reward,
        remainingToday: evalResult.remainingToday,
        outcome: evalResult.outcome,
      };
    });

    logger.info('claimInviteReward', { uid, inviteId, outcome: result.outcome });
    return result;
  },
);
