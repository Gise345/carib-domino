import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertNotBanned } from '../admin/bans';
import { assertNotGuest } from './entitlement';
import { assertNotMuted } from './mutes';
import { evaluateRateLimit } from './rateLimit';
import { filterProfanity } from './profanity';
import { REFUSAL_RATE_LIMITED, refusal } from './refusals';
import {
  ChatMember,
  MAX_MESSAGE_LENGTH,
  isValidRoomId,
  normalizeMessageText,
  retentionExpiry,
} from './model';

if (getApps().length === 0) {
  initializeApp();
}

const SendSchema = z.object({
  roomId: z.string().trim().min(4).max(64),
  // Generous outer bound; the real length rule runs after normalisation so a
  // message padded with whitespace can't slip past on raw length alone.
  text: z.string().min(1).max(2000),
});

/**
 * Posts one chat message (ADR 0023 §1). This is the ONLY write path into
 * `/chatRooms/**` — clients read with a snapshot listener and can write nothing.
 * Every gate runs here, in order: signed in, not banned, not a guest, a member of
 * the room, not muted, within the rate limit, within length — then the profanity
 * filter masks the delivered text while the verbatim original is kept for
 * moderators (never readable by a client).
 *
 * @returns `{ messageId, filtered }`
 */
export const sendChatMessage = onCall(
  async (request: CallableRequest<unknown>): Promise<{ messageId: string; filtered: boolean }> => {
    const uid = assertNotGuest(request);
    await assertNotBanned(uid);
    await assertNotMuted(uid);

    const parsed = SendSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid sendChatMessage payload.');
    }
    const { roomId } = parsed.data;
    if (!isValidRoomId(roomId)) {
      throw new HttpsError('invalid-argument', 'Invalid room id.');
    }

    const original = normalizeMessageText(parsed.data.text);
    if (original.length === 0) {
      throw new HttpsError('invalid-argument', 'Message is empty.');
    }
    if (original.length > MAX_MESSAGE_LENGTH) {
      throw new HttpsError(
        'invalid-argument',
        `Messages are limited to ${String(MAX_MESSAGE_LENGTH)} characters.`,
      );
    }

    const db = getFirestore();
    const roomRef = db.collection('chatRooms').doc(roomId);
    const roomSnap = await roomRef.get();
    if (!roomSnap.exists) {
      throw new HttpsError('not-found', 'Chat room not found.');
    }
    const members = (roomSnap.data()?.['members'] ?? {}) as Record<string, ChatMember>;
    const me = members[uid];
    if (me === undefined) {
      throw new HttpsError('permission-denied', 'You are not in this chat room.');
    }

    // Rate limit: the sender's own window, so one flooder can't spend anyone
    // else's allowance. Transactional — two concurrent sends can't both pass.
    const limitRef = db.collection('chatRateLimits').doc(uid);
    const now = Date.now();
    await db.runTransaction(async (tx) => {
      const snap = await tx.get(limitRef);
      const recent = (snap.data()?.['window'] ?? []) as number[];
      const decision = evaluateRateLimit(recent, now);
      if (!decision.allowed) {
        throw new HttpsError(
          'resource-exhausted',
          refusal(REFUSAL_RATE_LIMITED, 'Slow down a moment.'),
          { code: REFUSAL_RATE_LIMITED, retryAfterMs: decision.retryAfterMs },
        );
      }
      tx.set(limitRef, { window: decision.window, updatedAt: new Date(now) });
    });

    const filter = filterProfanity(original);
    const messageRef = roomRef.collection('messages').doc();
    const batch = db.batch();
    batch.set(messageRef, {
      senderUid: uid,
      senderName: me.name,
      seat: me.seat,
      text: filter.text,
      filtered: filter.filtered,
      severe: filter.severe,
      redacted: false,
      createdAt: FieldValue.serverTimestamp(),
      // Swept by the TTL policy on the `messages` collection group. A report
      // freezes its own copy of the transcript, so evidence outlives the sweep.
      expiresAt: retentionExpiry(new Date(now)),
    });
    if (filter.filtered) {
      // Firestore rules are document-level: a field on a client-readable doc is
      // client-readable. The verbatim text therefore lives in its own
      // deny-all subcollection, where only moderators (via getChatReport) see it.
      batch.set(roomRef.collection('originals').doc(messageRef.id), {
        senderUid: uid,
        originalText: original,
        createdAt: FieldValue.serverTimestamp(),
        expiresAt: retentionExpiry(new Date(now)),
      });
    }
    const roomUpdate: Record<string, unknown> = {
      lastMessageAt: FieldValue.serverTimestamp(),
    };
    if (roomSnap.data()?.['retained'] !== true) {
      roomUpdate['expiresAt'] = retentionExpiry(new Date(now));
    }
    if (filter.severe) {
      roomUpdate['severeCount'] = FieldValue.increment(1);
    }
    batch.update(roomRef, roomUpdate);
    await batch.commit();

    return { messageId: messageRef.id, filtered: filter.filtered };
  },
);
