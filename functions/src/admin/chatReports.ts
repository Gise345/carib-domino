import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue, Timestamp } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';
import { writeAudit } from './audit';

if (getApps().length === 0) {
  initializeApp();
}

/** ISO string for a Firestore timestamp, or null. */
function iso(value: unknown): string | null {
  return value instanceof Timestamp ? value.toDate().toISOString() : null;
}

const ListSchema = z.object({
  status: z.enum(['open', 'actioned', 'dismissed', 'all']).default('open'),
  limit: z.number().int().min(1).max(100).default(50),
});

/** A row in the moderation queue — enough to triage without the transcript. */
export interface ChatReportSummary {
  id: string;
  status: string;
  reason: string;
  roomId: string;
  mode: string;
  reporterUid: string;
  reporterName: string;
  reportedUid: string;
  reportedName: string;
  reportedText: string;
  messageCount: number;
  severe: boolean;
  createdAt: string | null;
}

/**
 * Lists chat reports for the moderation queue, newest first (ADR 0023 §7).
 * Admin-gated; reports are deny-all to clients, so this callable is the only way
 * to see them.
 *
 * @param request - `{ status?, limit? }`
 * @returns `{ reports }` — summaries without transcripts
 */
export const listChatReports = onCall(
  async (request: CallableRequest<unknown>): Promise<{ reports: ChatReportSummary[] }> => {
    assertAdmin(request);
    const parsed = ListSchema.safeParse(request.data ?? {});
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid listChatReports payload.');
    }
    const { status, limit } = parsed.data;

    const db = getFirestore();
    let query = db.collection('chatReports').orderBy('createdAt', 'desc').limit(limit);
    if (status !== 'all') {
      query = db
        .collection('chatReports')
        .where('status', '==', status)
        .orderBy('createdAt', 'desc')
        .limit(limit);
    }

    const snap = await query.get();
    const reports = snap.docs.map((doc) => {
      const d = doc.data();
      const transcript = (d['transcript'] ?? []) as Record<string, unknown>[];
      return {
        id: doc.id,
        status: String(d['status'] ?? 'open'),
        reason: String(d['reason'] ?? 'other'),
        roomId: String(d['roomId'] ?? ''),
        mode: String(d['mode'] ?? 'unknown'),
        reporterUid: String(d['reporterUid'] ?? ''),
        reporterName: String(d['reporterName'] ?? ''),
        reportedUid: String(d['reportedUid'] ?? ''),
        reportedName: String(d['reportedName'] ?? ''),
        reportedText: String(d['reportedText'] ?? ''),
        messageCount: transcript.length,
        severe: transcript.some((line) => line['severe'] === true),
        createdAt: iso(d['createdAt']),
      };
    });

    return { reports };
  },
);

const GetSchema = z.object({ reportId: z.string().trim().min(1).max(300) });

/**
 * Returns one report in full: the frozen, unmasked transcript, the room roster,
 * the ruleset and the server-issued match ids, plus the live moderation state of
 * the reported account (banned / muted) so a moderator can act in one screen.
 *
 * @param request - `{ reportId }`
 * @returns the report, its transcript, and the reported account's status
 */
export const getChatReport = onCall(
  async (request: CallableRequest<unknown>): Promise<Record<string, unknown>> => {
    assertAdmin(request);
    const parsed = GetSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid getChatReport payload.');
    }

    const db = getFirestore();
    const snap = await db.collection('chatReports').doc(parsed.data.reportId).get();
    if (!snap.exists) {
      throw new HttpsError('not-found', 'Report not found.');
    }
    const d = snap.data() ?? {};
    const reportedUid = String(d['reportedUid'] ?? '');

    const [banSnap, muteSnap, priorSnap] = await Promise.all([
      db.collection('bans').doc(reportedUid).get(),
      db.collection('chatMutes').doc(reportedUid).get(),
      db.collection('chatReports').where('reportedUid', '==', reportedUid).limit(50).get(),
    ]);

    const transcript = ((d['transcript'] ?? []) as Record<string, unknown>[]).map((line) => ({
      ...line,
      at: iso(line['at']),
    }));

    return {
      id: snap.id,
      status: String(d['status'] ?? 'open'),
      reason: String(d['reason'] ?? 'other'),
      note: String(d['note'] ?? ''),
      roomId: String(d['roomId'] ?? ''),
      mode: String(d['mode'] ?? 'unknown'),
      matchIds: d['matchIds'] ?? [],
      members: d['members'] ?? {},
      reporterUid: String(d['reporterUid'] ?? ''),
      reporterName: String(d['reporterName'] ?? ''),
      reportedUid,
      reportedName: String(d['reportedName'] ?? ''),
      reportedMessageId: String(d['reportedMessageId'] ?? ''),
      reportedText: String(d['reportedText'] ?? ''),
      transcript,
      createdAt: iso(d['createdAt']),
      resolvedAt: iso(d['resolvedAt']),
      resolvedByEmail: d['resolvedByEmail'] ?? null,
      resolution: d['resolution'] ?? null,
      // Live moderation state of the reported account.
      isBanned: banSnap.exists,
      muteUntil: muteSnap.exists ? iso(muteSnap.data()?.['until']) : null,
      priorReportCount: priorSnap.size,
    };
  },
);

const ResolveSchema = z.object({
  reportId: z.string().trim().min(1).max(300),
  resolution: z.enum(['actioned', 'dismissed']),
  note: z.string().trim().max(500).default(''),
});

/**
 * Closes a report. The punishment itself (mute / ban / redaction) runs through
 * its own audited callable; this records the decision so the queue clears and the
 * outcome is attributable.
 *
 * @param request - `{ reportId, resolution, note? }`
 * @returns `{ resolved: true }`
 */
export const resolveChatReport = onCall(
  async (request: CallableRequest<unknown>): Promise<{ resolved: boolean }> => {
    const actor = assertAdmin(request);
    const parsed = ResolveSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid resolveChatReport payload.');
    }
    const { reportId, resolution, note } = parsed.data;

    const ref = getFirestore().collection('chatReports').doc(reportId);
    if (!(await ref.get()).exists) {
      throw new HttpsError('not-found', 'Report not found.');
    }
    await ref.update({
      status: resolution,
      resolution,
      resolutionNote: note,
      resolvedAt: FieldValue.serverTimestamp(),
      resolvedByUid: actor.uid,
      resolvedByEmail: actor.email,
    });

    await writeAudit(actor, 'resolve_chat_report', reportId, { resolution, note });
    return { resolved: true };
  },
);

const RedactSchema = z.object({
  roomId: z.string().trim().min(4).max(64),
  messageId: z.string().trim().min(1).max(128),
});

/**
 * Removes a message from the live room for everyone still in it. The frozen
 * report transcript keeps the original, so redaction destroys no evidence.
 *
 * @param request - `{ roomId, messageId }`
 * @returns `{ redacted: true }`
 */
export const redactChatMessage = onCall(
  async (request: CallableRequest<unknown>): Promise<{ redacted: boolean }> => {
    const actor = assertAdmin(request);
    const parsed = RedactSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid redactChatMessage payload.');
    }
    const { roomId, messageId } = parsed.data;

    const ref = getFirestore()
      .collection('chatRooms')
      .doc(roomId)
      .collection('messages')
      .doc(messageId);
    if (!(await ref.get()).exists) {
      throw new HttpsError('not-found', 'Message not found.');
    }
    await ref.update({ text: '', redacted: true });

    await writeAudit(actor, 'redact_chat_message', messageId, { roomId });
    return { redacted: true };
  },
);
