/**
 * Shared chat types + limits (ADR 0023). Pure — no Firestore, no Firebase
 * imports — so every limit is unit-testable and both the callables and the tests
 * read the same numbers.
 */

/**
 * Longest message a player may send. Long enough for banter, short enough that a
 * wall of text can't be used to bury the room.
 */
export const MAX_MESSAGE_LENGTH = 200;

/** Seats in a room; the chat roster can never exceed the table. */
export const MAX_ROOM_MEMBERS = 4;

/** Days a room (and its messages) survive without moderation interest. */
export const ROOM_RETENTION_DAYS = 30;

/** Messages copied into a report as frozen evidence. */
export const REPORT_TRANSCRIPT_LIMIT = 100;

/** Rate limit: no more than this many messages inside {@link RATE_WINDOW_MS}. */
export const RATE_MAX_IN_WINDOW = 5;

/** Sliding window the message allowance is measured over. */
export const RATE_WINDOW_MS = 10_000;

/** Minimum gap between two messages — blocks hold-to-send macro spam. */
export const RATE_MIN_GAP_MS = 700;

/** Shape of a `/chatRooms/{roomId}` member entry. */
export interface ChatMember {
  /** Display name captured at join time, so a later rename can't rewrite history. */
  readonly name: string;
  /** Table seat index, or -1 when unknown. */
  readonly seat: number;
}

/** Report lifecycle. `open` needs a decision; the rest are terminal. */
export type ChatReportStatus = 'open' | 'actioned' | 'dismissed';

/** Why a player flagged a message. A closed set, so the queue stays filterable. */
export const REPORT_REASONS = [
  'harassment',
  'hate',
  'threats',
  'sexual',
  'spam',
  'cheating',
  'other',
] as const;

export type ChatReportReason = (typeof REPORT_REASONS)[number];

/**
 * Room id shape. The Photon session name — either a joinable room code or a
 * Photon-assigned session id — so one chat room spans a whole series.
 */
const ROOM_ID_PATTERN = /^[A-Za-z0-9_-]{4,64}$/;

/**
 * Whether a client-supplied room id is well formed. Rejects path separators and
 * oversized ids before they ever reach a Firestore document path.
 *
 * @param roomId - the candidate id
 * @returns true when the id is safe to use as a document id
 */
export function isValidRoomId(roomId: string): boolean {
  return ROOM_ID_PATTERN.test(roomId);
}

/** Firestore auto-ids, and anything else safe to concatenate into a path. */
const DOC_ID_PATTERN = /^[A-Za-z0-9_-]{1,128}$/;

/**
 * Whether a client-supplied document id is safe to use in a path. Without this a
 * value like `"a/b"` would silently address a different collection, or be
 * concatenated into a compound id that does.
 *
 * @param id - the candidate id
 * @returns true when the id is a single safe path segment
 */
export function isValidDocId(id: string): boolean {
  return DOC_ID_PATTERN.test(id);
}

/**
 * Normalises a submitted message: strips control characters, collapses runs of
 * whitespace (including the newline padding used to shout), and trims.
 *
 * @param raw - the client's text
 * @returns the cleaned text, possibly empty
 */
export function normalizeMessageText(raw: string): string {
  return (
    raw
      // Control characters are exactly what we are stripping here.
      // eslint-disable-next-line no-control-regex
      .replace(/[\u0000-\u001F\u007F]/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
  );
}

/**
 * The retention stamp a room carries while nothing has been reported against it.
 *
 * @param now - current time
 * @returns the date the room and its messages become sweepable
 */
export function retentionExpiry(now: Date): Date {
  return new Date(now.getTime() + ROOM_RETENTION_DAYS * 24 * 60 * 60 * 1000);
}
