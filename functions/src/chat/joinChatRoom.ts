import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertNotBanned } from '../admin/bans';
import { isGuestToken } from './entitlement';
import { ChatMember, MAX_ROOM_MEMBERS, isValidRoomId, retentionExpiry } from './model';

if (getApps().length === 0) {
  initializeApp();
}

const JoinSchema = z.object({
  roomId: z.string().trim().min(4).max(64),
  /** Display name shown beside the caller's messages. Captured once, at join. */
  displayName: z.string().trim().min(1).max(40).default('Player'),
  /** Table seat, for per-seat colouring in the client. */
  seat: z.number().int().min(-1).max(3).default(-1),
  /** The server-issued match id being played, for moderation context. */
  matchId: z.string().trim().max(128).optional(),
  /** Ruleset being played, e.g. "cutthroat" / "partner". */
  mode: z.string().trim().max(32).optional(),
});

/**
 * Joins the caller to a match's chat room, creating the room on first join
 * (ADR 0023 §2). The caller claims MEMBERSHIP FOR THEIR OWN authenticated uid
 * only — never a roster supplied by a host — which is what makes room membership,
 * and therefore the Firestore read rule, trustworthy.
 *
 * Guests may join (they are allowed to READ a room); `sendChatMessage` is what
 * refuses them. The room id is the Photon session name, so one room spans every
 * round of a series.
 *
 * @returns `{ roomId, memberCount, canSend }`
 */
export const joinChatRoom = onCall(
  async (
    request: CallableRequest<unknown>,
  ): Promise<{ roomId: string; memberCount: number; canSend: boolean }> => {
    const uid = request.auth?.uid;
    if (uid === undefined) {
      throw new HttpsError('unauthenticated', 'Sign-in required to join chat.');
    }
    await assertNotBanned(uid);

    const parsed = JoinSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid joinChatRoom payload.');
    }
    const { roomId, displayName, seat, matchId, mode } = parsed.data;
    if (!isValidRoomId(roomId)) {
      throw new HttpsError('invalid-argument', 'Invalid room id.');
    }

    const isGuest = isGuestToken(request.auth?.token);
    const db = getFirestore();
    const roomRef = db.collection('chatRooms').doc(roomId);
    const member: ChatMember = { name: displayName, seat };

    const memberCount = await db.runTransaction(async (tx) => {
      const snap = await tx.get(roomRef);
      const now = new Date();

      if (!snap.exists) {
        tx.set(roomRef, {
          members: { [uid]: member },
          mode: mode ?? 'unknown',
          matchIds: matchId !== undefined ? [matchId] : [],
          createdAt: FieldValue.serverTimestamp(),
          lastMessageAt: null,
          retained: false,
          expiresAt: retentionExpiry(now),
        });
        return 1;
      }

      const data = snap.data() ?? {};
      const members = (data['members'] ?? {}) as Record<string, ChatMember>;
      const alreadyIn = Object.prototype.hasOwnProperty.call(members, uid);
      if (!alreadyIn && Object.keys(members).length >= MAX_ROOM_MEMBERS) {
        throw new HttpsError('resource-exhausted', 'This chat room is full.');
      }

      const update: Record<string, unknown> = { [`members.${uid}`]: member };
      if (matchId !== undefined) {
        update['matchIds'] = FieldValue.arrayUnion(matchId);
      }
      // A room held for moderation keeps its cleared TTL.
      if (data['retained'] !== true) {
        update['expiresAt'] = retentionExpiry(new Date());
      }
      tx.update(roomRef, update);
      return alreadyIn ? Object.keys(members).length : Object.keys(members).length + 1;
    });

    logger.info('joinChatRoom', { roomId, uid, memberCount, isGuest });
    return { roomId, memberCount, canSend: !isGuest };
  },
);
