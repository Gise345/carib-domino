import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertNotBanned } from '../admin/bans';
import { assertNotGuest } from '../chat/entitlement';
import { assertNotMuted } from '../chat/mutes';
import { ChatMember, MAX_ROOM_MEMBERS, isValidRoomId, retentionExpiry } from '../chat/model';
import { VivoxClientConfig, isVivoxProvisioned, vivoxClientConfig } from './config';
import { REFUSAL_VOICE_DISABLED } from './model';
import { voiceChannelName } from './vivoxUri';

if (getApps().length === 0) {
  initializeApp();
}

const JoinVoiceSchema = z.object({
  roomId: z.string().trim().min(4).max(64),
  /** Display name shown beside the caller. Captured once, at join. */
  displayName: z.string().trim().min(1).max(40).default('Player'),
  /** Table seat, so a speaking indicator and a report can name a seat. */
  seat: z.number().int().min(-1).max(3).default(-1),
  /** The server-issued match id being played, for moderation context. */
  matchId: z.string().trim().max(128).optional(),
  /** Ruleset being played, e.g. "cutthroat" / "partner". */
  mode: z.string().trim().max(32).optional(),
  /**
   * How the caller reached this table. Recorded for analytics and cost
   * attribution ONLY — it is client-asserted and deliberately not a gate. Voice
   * scope is a product decision delivered by Remote Config (ADR 0024 §5); the
   * gates that actually matter — guest, ban, mute, membership — all come from
   * the signed token and server-read documents.
   */
  entry: z.enum(['code', 'quickmatch']).default('code'),
});

/** What the client needs to bring up a voice session. */
interface JoinVoiceResult {
  readonly roomId: string;
  readonly channelName: string;
  readonly canSpeak: boolean;
  readonly memberCount: number;
  readonly vivox: VivoxClientConfig;
}

/**
 * Admits the caller to a match's voice channel (ADR 0024 §4).
 *
 * Deliberately the same shape as `joinChatRoom`: the caller claims membership for
 * their OWN authenticated uid, never a host-supplied roster, which is what makes
 * the membership map trustworthy enough for `mintVivoxToken` to authorise
 * against. It writes into the SAME `/chatRooms/{roomId}` document rather than a
 * parallel collection, so voice inherits the room's retention TTL, its
 * `retained: true` moderation hold, and its read-if-member rule unchanged.
 *
 * Unlike chat, a guest is refused outright — they neither speak nor listen
 * (ADR 0024 §3). Chat's read-only guest access is not a precedent: text you can
 * see is text you can report, whereas a voice you have no participant handle for
 * cannot be reported at all.
 *
 * @returns `{ roomId, channelName, canSpeak, memberCount, vivox }`
 */
export const joinVoiceRoom = onCall(
  async (request: CallableRequest<unknown>): Promise<JoinVoiceResult> => {
    const uid = assertNotGuest(request);
    await assertNotBanned(uid);
    await assertNotMuted(uid);

    const parsed = JoinVoiceSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid joinVoiceRoom payload.');
    }
    const { roomId, displayName, seat, matchId, mode, entry } = parsed.data;
    if (!isValidRoomId(roomId)) {
      throw new HttpsError('invalid-argument', 'Invalid room id.');
    }

    // Until the manual Vivox provisioning in ADR 0024 is done, say so plainly
    // rather than admitting the player to a channel they cannot get a token for.
    const vivox = vivoxClientConfig();
    if (!isVivoxProvisioned(vivox)) {
      throw new HttpsError('failed-precondition', 'Voice is not available yet.', {
        code: REFUSAL_VOICE_DISABLED,
      });
    }

    const channelName = voiceChannelName(roomId);
    const db = getFirestore();
    const roomRef = db.collection('chatRooms').doc(roomId);
    const member: ChatMember = { name: displayName, seat };

    const memberCount = await db.runTransaction(async (tx) => {
      const snap = await tx.get(roomRef);
      const now = new Date();

      if (!snap.exists) {
        // Voice may reach the room before chat does; whichever arrives first
        // creates it, with identical fields either way.
        tx.set(roomRef, {
          members: { [uid]: member },
          voice: { [uid]: { joinedAt: now, canSpeak: true } },
          voiceChannel: channelName,
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
        throw new HttpsError('resource-exhausted', 'This room is full.');
      }

      const update: Record<string, unknown> = {
        [`members.${uid}`]: member,
        [`voice.${uid}`]: { joinedAt: now, canSpeak: true },
        voiceChannel: channelName,
      };
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

    logger.info('joinVoiceRoom', { roomId, uid, memberCount, entry, mode });
    return { roomId, channelName, canSpeak: true, memberCount, vivox };
  },
);
