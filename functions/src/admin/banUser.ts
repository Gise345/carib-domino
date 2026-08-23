import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { getAuth } from 'firebase-admin/auth';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';
import { writeAudit } from './audit';

if (getApps().length === 0) {
  initializeApp();
}

const BanSchema = z.object({
  uid: z.string().min(1).max(128),
  reason: z.string().trim().max(500).default(''),
});

/**
 * Bans a player (ADR 0022, phase D). Writes the authoritative `/bans/{uid}` record
 * (the gate gameplay functions check), best-effort marks a `banned` claim + revokes
 * refresh tokens so the client notices, and audits the action. Admin-gated; you
 * can't ban yourself.
 *
 * @returns `{ banned: true }`.
 */
export const banUser = onCall(
  async (request: CallableRequest<unknown>): Promise<{ banned: boolean }> => {
    const actor = assertAdmin(request);
    const parsed = BanSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid ban request.');
    }
    const { uid, reason } = parsed.data;
    if (uid === actor.uid) {
      throw new HttpsError('failed-precondition', 'You cannot ban yourself.');
    }

    const db = getFirestore();
    await db.collection('bans').doc(uid).set({
      reason,
      bannedByUid: actor.uid,
      bannedByEmail: actor.email,
      at: FieldValue.serverTimestamp(),
    });

    try {
      // Best-effort — the /bans doc is the authoritative gate regardless.
      await getAuth().setCustomUserClaims(uid, { banned: true });
      await getAuth().revokeRefreshTokens(uid);
    } catch {
      // No Auth user for this uid — the ban is still enforced via /bans.
    }

    await writeAudit(actor, 'ban_user', uid, { reason });
    return { banned: true };
  },
);
