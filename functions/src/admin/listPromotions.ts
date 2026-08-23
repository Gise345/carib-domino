import { onCall, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, DocumentData, Timestamp } from 'firebase-admin/firestore';
import { assertAdmin } from './requireAdmin';

if (getApps().length === 0) {
  initializeApp();
}

interface PromoRow {
  code: string;
  coins: number;
  active: boolean;
  expiresAtMs: number;
  maxRedemptions: number;
  redemptionCount: number;
}

function num(data: DocumentData, field: string): number {
  const v: unknown = data[field];
  return typeof v === 'number' ? v : 0;
}

function expiresMs(data: DocumentData): number {
  const v: unknown = data['expiresAt'];
  return v instanceof Timestamp ? v.toMillis() : 0;
}

/**
 * Lists promotions for the admin dashboard, newest first (ADR 0022, phase E).
 * Admin-gated, read-only. Timestamps are returned as epoch ms for the client.
 *
 * @returns `{ promotions }`.
 */
export const listPromotions = onCall(
  async (request: CallableRequest<unknown>): Promise<{ promotions: PromoRow[] }> => {
    assertAdmin(request);
    const snap = await getFirestore()
      .collection('promotions')
      .orderBy('createdAt', 'desc')
      .limit(200)
      .get();

    const promotions: PromoRow[] = snap.docs.map((d) => {
      const data = d.data();
      const active: unknown = data['active'];
      return {
        code: d.id,
        coins: num(data, 'coins'),
        active: active === true,
        expiresAtMs: expiresMs(data),
        maxRedemptions: num(data, 'maxRedemptions'),
        redemptionCount: num(data, 'redemptionCount'),
      };
    });
    return { promotions };
  },
);
