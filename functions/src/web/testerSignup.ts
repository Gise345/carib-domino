import { onRequest } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { createHash } from 'node:crypto';
import { z } from 'zod';

if (getApps().length === 0) {
  initializeApp();
}

/** Longest address permitted by RFC 5321. */
const MAX_EMAIL_LENGTH = 254;

/** Free-text "where do you play" cap — long enough for a city, short enough to be harmless. */
const MAX_COUNTRY_LENGTH = 60;

/** Rejects oversized bodies before Zod ever sees them. */
const MAX_BODY_BYTES = 4096;

/**
 * Payload from the marketing site's tester signup form.
 *
 * `nickname` is a honeypot: it is hidden from people via CSS and left empty by
 * every real submission, so anything in it marks the request as a bot.
 */
const SignupSchema = z.object({
  email: z.string().trim().toLowerCase().email().max(MAX_EMAIL_LENGTH),
  platforms: z
    .array(z.enum(['android', 'ios']))
    .min(1)
    .max(2),
  country: z.string().trim().max(MAX_COUNTRY_LENGTH).optional(),
  nickname: z.string().max(MAX_COUNTRY_LENGTH).optional(),
});

/** Parsed, normalised signup ready to persist. */
export type TesterSignup = z.infer<typeof SignupSchema>;

/**
 * Derives a stable document ID from a normalised email so a person who submits
 * the form twice updates their record instead of creating a duplicate seat.
 *
 * @param email Normalised (trimmed, lowercased) email address.
 * @returns Hex SHA-256 digest, used as the Firestore document ID.
 */
export function signupDocId(email: string): string {
  return createHash('sha256').update(email).digest('hex');
}

/**
 * Deduplicates and sorts the platform list so `['ios','android','ios']` and
 * `['android','ios']` produce identical stored records.
 *
 * @param platforms Validated platform values from the form.
 * @returns Sorted, unique platform list.
 */
export function normalisePlatforms(platforms: readonly ('android' | 'ios')[]): string[] {
  return [...new Set(platforms)].sort();
}

/**
 * Public HTTPS endpoint behind the Firebase Hosting rewrite `/api/tester-signup`.
 *
 * Collects soft-launch tester signups from caribbeandominos.com. Writes go
 * through the Admin SDK to `testerSignups/{sha256(email)}`, a collection that
 * Firestore rules deny to every client — the marketing site has no direct
 * Firestore access, in line with the trust model in `docs/ARCHITECTURE.md`.
 *
 * Responds 200 on success, 400 on a malformed payload, 405 on a non-POST, and
 * 500 if the write fails. Bot submissions caught by the honeypot receive a 200
 * and are discarded without a write, so the bot has no signal to tune against.
 */
export const testerSignup = onRequest(
  { maxInstances: 3, cors: false, invoker: 'public' },
  async (req, res): Promise<void> => {
    if (req.method !== 'POST') {
      res.set('Allow', 'POST');
      res.status(405).json({ error: 'Use POST.' });
      return;
    }

    if (req.rawBody.length > MAX_BODY_BYTES) {
      res.status(400).json({ error: 'Payload too large.' });
      return;
    }

    const parsed = SignupSchema.safeParse(req.body);
    if (!parsed.success) {
      res.status(400).json({ error: 'Check your email address and platform selection.' });
      return;
    }

    const { email, platforms, country, nickname } = parsed.data;

    // Honeypot tripped — look successful, write nothing.
    if (nickname !== undefined && nickname.length > 0) {
      logger.info('testerSignup honeypot tripped', { hasCountry: country !== undefined });
      res.status(200).json({ ok: true });
      return;
    }

    try {
      const db = getFirestore();
      const ref = db.collection('testerSignups').doc(signupDocId(email));
      const existing = await ref.get();

      await ref.set(
        {
          email,
          platforms: normalisePlatforms(platforms),
          ...(country !== undefined && country.length > 0 ? { country } : {}),
          ...(existing.exists ? {} : { createdAt: FieldValue.serverTimestamp() }),
          updatedAt: FieldValue.serverTimestamp(),
          source: 'caribbeandominos.com',
        },
        { merge: true },
      );

      logger.info('testerSignup stored', {
        returning: existing.exists,
        platforms: normalisePlatforms(platforms),
      });

      res.status(200).json({ ok: true });
    } catch (err) {
      logger.error('testerSignup write failed', { err });
      res.status(500).json({ error: 'Could not save your details. Please try again.' });
    }
  },
);
