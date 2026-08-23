import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue, Timestamp } from 'firebase-admin/firestore';
import { z } from 'zod';
import {
  ChatMember,
  REPORT_REASONS,
  REPORT_TRANSCRIPT_LIMIT,
  isValidRoomId,
  normalizeMessageText,
} from './model';

if (getApps().length === 0) {
  initializeApp();
}

const ReportSchema = z.object({
  roomId: z.string().trim().min(4).max(64),
  messageId: z.string().trim().min(1).max(128),
  reason: z.enum(REPORT_REASONS),
  note: z.string().trim().max(500).default(''),
});

/** One frozen line of the transcript stored on the report. */
interface TranscriptLine {
  messageId: string;
  senderUid: string;
  senderName: string;
  /** Verbatim where the filter masked it, otherwise the delivered text. */
  text: string;
  filtered: boolean;
  severe: boolean;
  at: Timestamp | null;
  /** True for the message the report is about. */
  reported: boolean;
}

/**
 * Flags a chat message for moderation (ADR 0023 §5).
 *
 * The report FREEZES its evidence: up to {@link REPORT_TRANSCRIPT_LIMIT} messages
 * of that room — unmasked, since a moderator must see what was actually typed —
 * are copied into an immutable `/chatReports` document along with the room's
 * roster, ruleset and the server-issued match ids played. Evidence therefore
 * survives message redaction, room expiry and account deletion. The room's TTL is
 * cleared so the live transcript is held too.
 *
 * Only a member of the room may report, and only once per message per reporter.
 *
 * @returns `{ reportId, alreadyReported }`
 */
export const reportChatMessage = onCall(
  async (
    request: CallableRequest<unknown>,
  ): Promise<{ reportId: string; alreadyReported: boolean }> => {
    const uid = request.auth?.uid;
    if (uid === undefined) {
      throw new HttpsError('unauthenticated', 'Sign-in required to report.');
    }

    const parsed = ReportSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid report payload.');
    }
    const { roomId, messageId, reason, note } = parsed.data;
    if (!isValidRoomId(roomId)) {
      throw new HttpsError('invalid-argument', 'Invalid room id.');
    }

    const db = getFirestore();
    const roomRef = db.collection('chatRooms').doc(roomId);
    const roomSnap = await roomRef.get();
    if (!roomSnap.exists) {
      throw new HttpsError('not-found', 'Chat room not found.');
    }
    const room = roomSnap.data() ?? {};
    const members = (room['members'] ?? {}) as Record<string, ChatMember>;
    if (!Object.prototype.hasOwnProperty.call(members, uid)) {
      throw new HttpsError('permission-denied', 'You are not in this chat room.');
    }

    const messageSnap = await roomRef.collection('messages').doc(messageId).get();
    if (!messageSnap.exists) {
      throw new HttpsError('not-found', 'Message not found.');
    }
    const message = messageSnap.data() ?? {};
    const reportedUid = String(message['senderUid'] ?? '');
    if (reportedUid === uid) {
      throw new HttpsError('failed-precondition', 'You cannot report your own message.');
    }

    // One report per reporter per message — a deterministic id makes the
    // duplicate check free and race-proof.
    const reportId = `${roomId}_${messageId}_${uid}`;
    const reportRef = db.collection('chatReports').doc(reportId);
    if ((await reportRef.get()).exists) {
      return { reportId, alreadyReported: true };
    }

    const [messagesSnap, originalsSnap] = await Promise.all([
      roomRef
        .collection('messages')
        .orderBy('createdAt', 'desc')
        .limit(REPORT_TRANSCRIPT_LIMIT)
        .get(),
      roomRef.collection('originals').limit(REPORT_TRANSCRIPT_LIMIT).get(),
    ]);

    const originals = new Map<string, string>();
    for (const doc of originalsSnap.docs) {
      originals.set(doc.id, String(doc.data()['originalText'] ?? ''));
    }

    const transcript: TranscriptLine[] = messagesSnap.docs
      .map((doc) => {
        const d = doc.data();
        return {
          messageId: doc.id,
          senderUid: String(d['senderUid'] ?? ''),
          senderName: String(d['senderName'] ?? ''),
          text: originals.get(doc.id) ?? String(d['text'] ?? ''),
          filtered: d['filtered'] === true,
          severe: d['severe'] === true,
          at: (d['createdAt'] as Timestamp | undefined) ?? null,
          reported: doc.id === messageId,
        };
      })
      .reverse(); // oldest first, the way a moderator reads it

    await reportRef.set({
      status: 'open',
      reason,
      note: normalizeMessageText(note),
      roomId,
      mode: String(room['mode'] ?? 'unknown'),
      matchIds: (room['matchIds'] ?? []) as string[],
      members,
      reporterUid: uid,
      reporterName: members[uid]?.name ?? '',
      reportedUid,
      reportedName: String(message['senderName'] ?? ''),
      reportedMessageId: messageId,
      reportedText: originals.get(messageId) ?? String(message['text'] ?? ''),
      transcript,
      createdAt: FieldValue.serverTimestamp(),
      resolvedAt: null,
      resolvedByEmail: null,
      resolution: null,
    });

    // Hold the live room for moderators: a reported room is never TTL-swept.
    await roomRef.update({
      retained: true,
      expiresAt: FieldValue.delete(),
      reportCount: FieldValue.increment(1),
    });

    logger.info('reportChatMessage', { roomId, messageId, reportedUid, reporterUid: uid, reason });
    return { reportId, alreadyReported: false };
  },
);
