import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore } from 'firebase-admin/firestore';
import { getAuth } from 'firebase-admin/auth';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';
import { writeAudit } from './audit';

if (getApps().length === 0) {
  initializeApp();
}

const UnbanSchema = z.object({ uid: z.string().min(1).max(128) });

/**
 * Lifts a ban (ADR 0022, phase D): deletes `/bans/{uid}`, clears the `banned`
 * claim, and audits the action. Admin-gated.
 *
 * @returns `{ banned: false }`.
 */
export const unbanUser = onCall(
  async (request: CallableRequest<unknown>): Promise<{ banned: boolean }> => {
    const actor = assertAdmin(request);
    const parsed = UnbanSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid unban request.');
    }
    const { uid } = parsed.data;

    await getFirestore().collection('bans').doc(uid).delete();
    try {
      await getAuth().setCustomUserClaims(uid, {});
    } catch {
      // No Auth user — nothing to clear.
    }

    await writeAudit(actor, 'unban_user', uid);
    return { banned: false };
  },
);
