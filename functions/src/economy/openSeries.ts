import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { z } from 'zod';

if (getApps().length === 0) {
  initializeApp();
}

const OpenSeriesSchema = z
  .object({
    playerCount: z.number().int().min(2).max(4),
    mode: z.enum(['cutthroat', 'partner']).default('cutthroat'),
    format: z.enum(['classic', 'quick']).default('classic'),
  })
  .refine((v) => v.mode !== 'partner' || v.playerCount === 4, {
    message: 'Jamaican Partner requires exactly 4 players.',
  });

/**
 * Opens a server-side SERIES (M6): the authoritative record a whole match plays
 * out against — its roster (seat → authenticated uid), pot, and running
 * team-points. Rounds within the series each still fetch their own seed via
 * `startMatch` (tagged with this `seriesId`); settlement accumulates each
 * validated round here and pays the pot out when a team hits the target.
 *
 * The series doc is Cloud-Functions-only (Firestore denies client access);
 * clients learn the `seriesId` solely through this return value and then call
 * `joinSeries` to stake in and claim their seat.
 *
 * @returns `{ seriesId }`
 */
export const openSeries = onCall(
  async (request: CallableRequest<unknown>): Promise<{ seriesId: string }> => {
    if (!request.auth?.uid) {
      throw new HttpsError('unauthenticated', 'Sign-in required to open a series.');
    }

    const parsed = OpenSeriesSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError(
        'invalid-argument',
        `Invalid openSeries payload: ${parsed.error.message}`,
      );
    }

    const db = getFirestore();
    const ref = await db.collection('series').add({
      mode: parsed.data.mode,
      format: parsed.data.format,
      playerCount: parsed.data.playerCount,
      openerUid: request.auth.uid,
      roster: {}, // seat index (as string) -> uid, filled by joinSeries
      pot: 0,
      teamPoints: {}, // team id -> points, accumulated by settlement
      status: 'open', // 'open' | 'settled'
      createdAt: FieldValue.serverTimestamp(),
    });

    logger.info('openSeries created', {
      seriesId: ref.id,
      mode: parsed.data.mode,
      format: parsed.data.format,
      playerCount: parsed.data.playerCount,
    });

    return { seriesId: ref.id };
  },
);
