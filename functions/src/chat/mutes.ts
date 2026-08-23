/**
 * Chat mutes (ADR 0023 §7) — a time-boxed chat suspension that stops short of a
 * full account ban: the player keeps playing, but cannot send messages until the
 * mute expires. Written only by the admin callables, read only here.
 */

import { getFirestore, Timestamp } from 'firebase-admin/firestore';
import { HttpsError } from 'firebase-functions/v2/https';

/** Stored `/chatMutes/{uid}` document. */
export interface ChatMute {
  /** When the mute lapses. A permanent mute uses a far-future date. */
  readonly until: Timestamp | Date;
  readonly reason: string;
  readonly mutedByUid: string;
  readonly mutedByEmail: string;
}

/** Fields of a mute the expiry check needs, in either Firestore or plain form. */
export interface MuteLike {
  readonly until?: { toDate(): Date } | Date | undefined;
}

/**
 * Whether a mute document is still in force.
 *
 * @param mute - the stored mute, or undefined when none exists
 * @param now - current time
 * @returns true when the player may NOT send
 */
export function isMuteActive(mute: MuteLike | undefined, now: Date): boolean {
  const until = mute?.until;
  if (until === undefined) {
    return false;
  }
  const untilDate = until instanceof Date ? until : until.toDate();
  return untilDate.getTime() > now.getTime();
}

/**
 * Throws when the caller is currently muted from chat.
 *
 * @param uid - the sender's uid
 * @throws HttpsError('permission-denied') with code `muted` and the expiry
 */
export async function assertNotMuted(uid: string): Promise<void> {
  const snap = await getFirestore().collection('chatMutes').doc(uid).get();
  if (!snap.exists) {
    return;
  }
  const mute = snap.data() as MuteLike | undefined;
  if (isMuteActive(mute, new Date())) {
    const until = mute?.until;
    const untilDate = until instanceof Date ? until : until?.toDate();
    throw new HttpsError('permission-denied', 'You are muted in chat.', {
      code: 'muted',
      until: untilDate?.toISOString() ?? null,
    });
  }
}
