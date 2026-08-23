import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';
import { writeAudit } from './audit';

if (getApps().length === 0) {
  initializeApp();
}

/** Longest mute expressible here — a year. Beyond that, ban the account. */
const MAX_MUTE_HOURS = 24 * 365;

const MuteSchema = z.object({
  uid: z.string().trim().min(1).max(128),
  hours: z.number().int().min(1).max(MAX_MUTE_HOURS),
  reason: z.string().trim().max(500).default(''),
});

/**
 * Mutes a player in chat for a fixed period (ADR 0023 §7) — the proportionate
 * response to an insult, where a ban is the response to a threat. The player
 * keeps playing; `sendChatMessage` refuses their messages until the mute lapses.
 * Audited like every admin action.
 *
 * @param request - `{ uid, hours, reason? }`
 * @returns `{ muted: true, until }` (ISO)
 */
export const muteUser = onCall(
  async (request: CallableRequest<unknown>): Promise<{ muted: boolean; until: string }> => {
    const actor = assertAdmin(request);
    const parsed = MuteSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid mute request.');
    }
    const { uid, hours, reason } = parsed.data;
    if (uid === actor.uid) {
      throw new HttpsError('failed-precondition', 'You cannot mute yourself.');
    }

    const until = new Date(Date.now() + hours * 60 * 60 * 1000);
    await getFirestore().collection('chatMutes').doc(uid).set({
      until,
      reason,
      hours,
      mutedByUid: actor.uid,
      mutedByEmail: actor.email,
      at: FieldValue.serverTimestamp(),
    });

    await writeAudit(actor, 'mute_user', uid, { hours, reason, until: until.toISOString() });
    return { muted: true, until: until.toISOString() };
  },
);

const UnmuteSchema = z.object({ uid: z.string().trim().min(1).max(128) });

/**
 * Lifts a chat mute early.
 *
 * @param request - `{ uid }`
 * @returns `{ unmuted: true }`
 */
export const unmuteUser = onCall(
  async (request: CallableRequest<unknown>): Promise<{ unmuted: boolean }> => {
    const actor = assertAdmin(request);
    const parsed = UnmuteSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid unmute request.');
    }

    await getFirestore().collection('chatMutes').doc(parsed.data.uid).delete();
    await writeAudit(actor, 'unmute_user', parsed.data.uid);
    return { unmuted: true };
  },
);
