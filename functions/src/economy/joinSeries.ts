import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { assertNotBanned } from '../admin/bans';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue, Transaction } from 'firebase-admin/firestore';
import { z } from 'zod';
import { ENTRY_STAKE, canAfford } from '../lib/economy';
import { debit, readOrInitWallet } from '../wallet/wallet';

if (getApps().length === 0) {
  initializeApp();
}

const JoinSeriesSchema = z.object({
  seriesId: z.string().min(1),
  seat: z.number().int().min(0).max(3),
});

/**
 * Claims a seat in a series and stakes the entry (M6). The seat → uid mapping is
 * recorded from the caller's OWN authenticated uid — not host self-report — which
 * is the roster that closes the result→uid trust gap (ADR 0007). In one
 * transaction it: verifies the series is open, the seat is free (or already this
 * uid, making the call idempotent), the uid isn't already seated elsewhere, and
 * the wallet can cover {@link ENTRY_STAKE}; then debits the stake into the pot and
 * writes the roster entry.
 *
 * @returns `{ seat, balance }` — the claimed seat and the post-debit balance.
 */
export const joinSeries = onCall(
  async (request: CallableRequest<unknown>): Promise<{ seat: number; balance: number }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to join a series.');
    }

    const parsed = JoinSeriesSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError(
        'invalid-argument',
        `Invalid joinSeries payload: ${parsed.error.message}`,
      );
    }
    const { seriesId, seat } = parsed.data;
    const uid = request.auth.uid;
    await assertNotBanned(uid);

    const db = getFirestore();
    const seriesRef = db.collection('series').doc(seriesId);
    const seatKey = String(seat);

    const balance = await db.runTransaction(async (txn: Transaction): Promise<number> => {
      const snap = await txn.get(seriesRef);
      if (!snap.exists) {
        throw new HttpsError('not-found', `Unknown series ${seriesId}.`);
      }
      const data = snap.data() ?? {};
      if (data['status'] !== 'open') {
        throw new HttpsError('failed-precondition', 'Series is no longer open to join.');
      }

      const roster = (data['roster'] as Record<string, string> | undefined) ?? {};

      // Idempotent: this uid already holds this seat → no double-charge.
      if (roster[seatKey] === uid) {
        return readOrInitWallet(db, txn, uid);
      }
      if (roster[seatKey] !== undefined) {
        throw new HttpsError('already-exists', `Seat ${String(seat)} is already taken.`);
      }
      for (const [otherSeat, otherUid] of Object.entries(roster)) {
        if (otherUid === uid) {
          throw new HttpsError('already-exists', `Already seated at ${otherSeat} in this series.`);
        }
      }

      const before = await readOrInitWallet(db, txn, uid); // read before any write
      if (!canAfford(before, ENTRY_STAKE)) {
        throw new HttpsError('failed-precondition', 'Not enough coins for the entry stake.');
      }

      debit(db, txn, uid, ENTRY_STAKE);
      txn.set(
        seriesRef,
        { roster: { [seatKey]: uid }, pot: FieldValue.increment(ENTRY_STAKE) },
        { merge: true },
      );
      return before - ENTRY_STAKE;
    });

    logger.info('joinSeries staked', { seriesId, seat, uid, balance });
    return { seat, balance };
  },
);
