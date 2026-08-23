import { randomInt } from 'crypto';
import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore } from 'firebase-admin/firestore';
import { z } from 'zod';
import { assertNotBanned } from '../admin/bans';
import { assertNotGuest } from '../chat/entitlement';
import { assertNotMuted } from '../chat/mutes';
import { REFUSAL_RATE_LIMITED, refusal } from '../chat/refusals';
import { buildClaims, signAccessToken } from './accessToken';
import { authorizeTokenRequest } from './authorize';
import { VIVOX_TOKEN_KEY, isVivoxProvisioned, vivoxClientConfig } from './config';
import {
  REFUSAL_VOICE_DISABLED,
  VOICE_TOKEN_RATE_MAX,
  VOICE_TOKEN_TTL_MS,
  VOICE_TOKEN_WINDOW_MS,
  VOICE_ACTIONS,
} from './model';
import { channelUri, userUri } from './vivoxUri';

if (getApps().length === 0) {
  initializeApp();
}

const MintSchema = z.object({
  action: z.enum(VOICE_ACTIONS),
  /** Required for `join`, ignored for `login`. */
  roomId: z.string().trim().min(4).max(64).optional(),
});

/**
 * The uniqueness serial Vivox requires. Random rather than incrementing: Cloud
 * Functions instances are independent, so a per-instance counter would collide
 * across them.
 */
const SERIAL_MAX = 2_000_000_000;

/** Mints a Vivox access token for one action. */
interface MintResult {
  readonly token: string;
  readonly expiresAt: string;
}

/**
 * Signs one Vivox access token for the caller (ADR 0024 §4).
 *
 * Called by the client's `IVivoxTokenProvider` each time the SDK needs a
 * credential — once to log in, once to join the channel. Tokens are single-use
 * and short-lived, so there is nothing to refresh mid-match, and every
 * privileged action is independently re-authorised: a player banned or muted
 * during a series loses voice at their next token request.
 *
 * SECURITY. `GetTokenAsync` supplies the client's own idea of `fromUserUri` and
 * `channelUri`. This function never reads them — they are not in the schema, and
 * {@link authorizeTokenRequest} will not accept them. The `f` claim is built from
 * the uid on the verified token and the `t` claim from the channel recorded on
 * the room the caller is a proven member of. Signing client-supplied URIs would
 * mint a credential to join any channel as any user.
 *
 * @returns `{ token, expiresAt }`
 */
export const mintVivoxToken = onCall(
  { secrets: [VIVOX_TOKEN_KEY] },
  async (request: CallableRequest<unknown>): Promise<MintResult> => {
    const uid = assertNotGuest(request);
    await assertNotBanned(uid);
    await assertNotMuted(uid);

    const parsed = MintSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid mintVivoxToken payload.');
    }
    const { action, roomId } = parsed.data;

    const vivox = vivoxClientConfig();
    if (!isVivoxProvisioned(vivox)) {
      throw new HttpsError('failed-precondition', 'Voice is not available yet.', {
        code: REFUSAL_VOICE_DISABLED,
      });
    }

    const db = getFirestore();

    // Every mint is a billed credential, so the allowance is per-caller and
    // transactional — a looping client cannot churn tokens, and cannot spend
    // anyone else's allowance either.
    const now = Date.now();
    const limitRef = db.collection('voiceTokenLimits').doc(uid);
    await db.runTransaction(async (tx) => {
      const snap = await tx.get(limitRef);
      const recent = ((snap.data()?.['window'] ?? []) as number[]).filter(
        (t) => now - t < VOICE_TOKEN_WINDOW_MS,
      );
      if (recent.length >= VOICE_TOKEN_RATE_MAX) {
        throw new HttpsError(
          'resource-exhausted',
          refusal(REFUSAL_RATE_LIMITED, 'Too many voice connections.'),
          { code: REFUSAL_RATE_LIMITED },
        );
      }
      tx.set(limitRef, { window: [...recent, now], updatedAt: new Date(now) });
    });

    // Only a join needs the room, and only to prove membership and read back
    // the channel the SERVER recorded.
    let roomExists: boolean | undefined;
    let roomMembers: Record<string, unknown> | undefined;
    let roomVoiceChannel: string | undefined;
    if (action === 'join' && roomId !== undefined) {
      const snap = await db.collection('chatRooms').doc(roomId).get();
      roomExists = snap.exists;
      roomMembers = (snap.data()?.['members'] ?? {}) as Record<string, unknown>;
      roomVoiceChannel = snap.data()?.['voiceChannel'] as string | undefined;
    }

    const decision = authorizeTokenRequest({
      action,
      callerUid: uid,
      roomId,
      roomExists,
      roomMembers,
      roomVoiceChannel,
    });
    if (!decision.ok) {
      throw new HttpsError('permission-denied', refusal(decision.code, decision.message), {
        code: decision.code,
      });
    }

    const claims = buildClaims({
      issuer: vivox.issuer,
      action,
      fromUri: userUri(vivox.issuer, vivox.domain, decision.fromPlayerId),
      toUri:
        decision.channelName === null
          ? undefined
          : channelUri(vivox.issuer, vivox.domain, decision.channelName),
      nowMs: now,
      serial: randomInt(SERIAL_MAX),
    });

    return {
      token: signAccessToken(claims, VIVOX_TOKEN_KEY.value()),
      expiresAt: new Date(now + VOICE_TOKEN_TTL_MS).toISOString(),
    };
  },
);
