import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue, Timestamp } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';
import { writeAudit } from './audit';
import { normalizeCode } from './promotions';

if (getApps().length === 0) {
  initializeApp();
}

const CreateSchema = z.object({
  code: z.string().min(1).max(40),
  coins: z.number().int().min(1).max(1_000_000),
  expiresAtMs: z.number().int().positive().optional(),
  maxRedemptions: z.number().int().min(0).max(100_000_000).optional(),
});

/**
 * Creates a promo code (ADR 0022, phase E). Admin-gated + audited. Players redeem
 * it via `redeemPromoCode`, which credits coins server-side. Fails if the code
 * already exists (never resets a live promo's counters).
 *
 * @returns `{ code }` — the normalised code created.
 */
export const createPromotion = onCall(
  async (request: CallableRequest<unknown>): Promise<{ code: string }> => {
    const actor = assertAdmin(request);
    const parsed = CreateSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid promotion.');
    }
    const code = normalizeCode(parsed.data.code);
    if (code === null) {
      throw new HttpsError('invalid-argument', 'Code must be 3–32 letters or digits.');
    }
    const { coins, expiresAtMs, maxRedemptions } = parsed.data;

    try {
      await getFirestore()
        .collection('promotions')
        .doc(code)
        .create({
          code,
          coins,
          active: true,
          expiresAt: expiresAtMs === undefined ? null : Timestamp.fromMillis(expiresAtMs),
          maxRedemptions: maxRedemptions ?? 0,
          redemptionCount: 0,
          createdByUid: actor.uid,
          createdByEmail: actor.email,
          createdAt: FieldValue.serverTimestamp(),
        });
    } catch {
      // create() rejects if the doc already exists — don't clobber a live promo.
      throw new HttpsError('already-exists', `A promotion with code ${code} already exists.`);
    }

    await writeAudit(actor, 'create_promotion', code, {
      coins,
      expiresAtMs: expiresAtMs ?? 0,
      maxRedemptions: maxRedemptions ?? 0,
    });
    return { code };
  },
);
