import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';
import { writeAudit } from './audit';
import { normalizeCode } from './promotions';

if (getApps().length === 0) {
  initializeApp();
}

const ToggleSchema = z.object({ code: z.string().min(1).max(40), active: z.boolean() });

/**
 * Enables or disables a promo code (ADR 0022, phase E). Admin-gated + audited.
 * Disabling stops further redemptions without deleting the record.
 *
 * @returns `{ code, active }`.
 */
export const setPromotionActive = onCall(
  async (request: CallableRequest<unknown>): Promise<{ code: string; active: boolean }> => {
    const actor = assertAdmin(request);
    const parsed = ToggleSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid request.');
    }
    const code = normalizeCode(parsed.data.code);
    if (code === null) {
      throw new HttpsError('invalid-argument', 'Invalid code.');
    }

    const ref = getFirestore().collection('promotions').doc(code);
    const snap = await ref.get();
    if (!snap.exists) {
      throw new HttpsError('not-found', 'No such promotion.');
    }
    await ref.update({ active: parsed.data.active, updatedAt: FieldValue.serverTimestamp() });
    await writeAudit(actor, 'set_promotion_active', code, { active: parsed.data.active });
    return { code, active: parsed.data.active };
  },
);
