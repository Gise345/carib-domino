import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import {
  getFirestore,
  DocumentData,
  FieldValue,
  Timestamp,
  Transaction,
} from 'firebase-admin/firestore';
import { z } from 'zod';
import { credit, readOrInitWallet } from '../wallet/wallet';
import { assertNotBanned } from '../admin/bans';
import { evaluateRedemption, normalizeCode, PromoState } from '../admin/promotions';

if (getApps().length === 0) {
  initializeApp();
}

const RedeemSchema = z.object({ code: z.string().min(1).max(40) });

function num(data: DocumentData, field: string): number {
  const v: unknown = data[field];
  return typeof v === 'number' ? v : 0;
}

function toPromoState(data: DocumentData): PromoState {
  const active: unknown = data['active'];
  const expiresAt: unknown = data['expiresAt'];
  return {
    active: active === true,
    coins: num(data, 'coins'),
    expiresAtMs: expiresAt instanceof Timestamp ? expiresAt.toMillis() : 0,
    maxRedemptions: num(data, 'maxRedemptions'),
    redemptionCount: num(data, 'redemptionCount'),
  };
}

/**
 * Redeems a promo code for the signed-in player (ADR 0022, phase E). Server-
 * authoritative and abuse-resistant: one transaction reads the promo + this
 * player's redemption record + wallet, validates via {@link evaluateRedemption}
 * (active / not expired / not over the cap / not already redeemed), then credits
 * coins, records the redemption, and bumps the count. Banned users are blocked.
 *
 * @returns `{ rewarded, coins, newBalance, reason }`.
 */
export const redeemPromoCode = onCall(
  async (
    request: CallableRequest<unknown>,
  ): Promise<{ rewarded: boolean; coins: number; newBalance: number; reason: string }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to redeem a code.');
    }
    const uid = request.auth.uid;
    await assertNotBanned(uid);

    const parsed = RedeemSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid code.');
    }
    const code = normalizeCode(parsed.data.code);
    if (code === null) {
      throw new HttpsError('invalid-argument', 'Invalid code.');
    }

    const db = getFirestore();
    const promoRef = db.collection('promotions').doc(code);
    const redemptionRef = db.collection('promoRedemptions').doc(`${code}__${uid}`);

    const result = await db.runTransaction(async (txn: Transaction) => {
      // All reads before any write.
      const promoSnap = await txn.get(promoRef);
      if (!promoSnap.exists) {
        throw new HttpsError('not-found', "That code isn't valid.");
      }
      const redemptionSnap = await txn.get(redemptionRef);
      const balanceBefore = await readOrInitWallet(db, txn, uid);

      const promo = toPromoState(promoSnap.data() ?? {});
      const evalResult = evaluateRedemption(promo, Date.now(), redemptionSnap.exists);
      if (!evalResult.ok) {
        throw new HttpsError('failed-precondition', evalResult.reason);
      }

      credit(db, txn, uid, evalResult.coins);
      txn.set(redemptionRef, {
        code,
        uid,
        coins: evalResult.coins,
        at: FieldValue.serverTimestamp(),
      });
      txn.update(promoRef, { redemptionCount: FieldValue.increment(1) });

      return {
        rewarded: true,
        coins: evalResult.coins,
        newBalance: balanceBefore + evalResult.coins,
        reason: 'ok',
      };
    });

    logger.info('redeemPromoCode', { uid, code, coins: result.coins });
    return result;
  },
);
