/**
 * Vivox access token construction and signing (ADR 0024 §4). Pure, with the key
 * injected, so the whole format is unit-testable without ever touching Secret
 * Manager.
 *
 * A Vivox access token looks like a JWT but is not one: the header is a literal
 * empty object rather than `{"alg":...,"typ":...}`, and the claim set is Vivox's
 * own (`vxa`, `vxi`) rather than the registered JWT claims. It is
 * `base64url({}) . base64url(claims) . base64url(HMAC-SHA256(header + "." +
 * claims))`.
 */

import { createHmac } from 'crypto';
import { VOICE_TOKEN_TTL_MS, VoiceAction } from './model';

/**
 * The token header, pre-encoded. Vivox's header is always the empty JSON object
 * `{}`, whose base64url form is `e30` — there is nothing to vary, so it is a
 * constant rather than something we serialise every time.
 */
const ENCODED_HEADER = 'e30';

/** The claim set Vivox reads out of an access token. */
export interface VivoxClaims {
  /** Application-specific issuer, from the Vivox credentials. */
  readonly iss: string;
  /** Uniqueness guarantee — Vivox rejects a replayed serial. */
  readonly vxi: number;
  /** The action this token authorises. */
  readonly vxa: VoiceAction;
  /** Expiry, as epoch SECONDS (not milliseconds). */
  readonly exp: number;
  /** "From" — the SIP URI of the player performing the action. */
  readonly f: string;
  /** "To" — the channel SIP URI. Present for `join`, absent for `login`. */
  readonly t?: string;
}

/** Everything needed to build a claim set. */
export interface ClaimInput {
  readonly issuer: string;
  readonly action: VoiceAction;
  /** The caller's SIP URI. Always derived server-side, never client-supplied. */
  readonly fromUri: string;
  /** The channel SIP URI, required for `join` and ignored for `login`. */
  readonly toUri?: string | undefined;
  /** Current time, injected so expiry is testable. */
  readonly nowMs: number;
  /** Uniqueness serial, injected so tokens are reproducible in tests. */
  readonly serial: number;
  /** Override the default lifetime. */
  readonly ttlMs?: number;
}

/**
 * Builds the claim set for a token.
 *
 * A `login` token deliberately carries no `t` claim — it authorises connecting as
 * a player, not entering anywhere — while a `join` token must carry the channel
 * it admits the player to.
 *
 * @param input - the claim inputs
 * @returns the claims, ready to sign
 * @throws Error when a `join` is requested without a channel URI
 */
export function buildClaims(input: ClaimInput): VivoxClaims {
  const { issuer, action, fromUri, toUri, nowMs, serial, ttlMs = VOICE_TOKEN_TTL_MS } = input;

  const claims: VivoxClaims = {
    iss: issuer,
    vxi: serial,
    vxa: action,
    exp: Math.floor((nowMs + ttlMs) / 1000),
    f: fromUri,
  };

  if (action !== 'join') {
    return claims;
  }

  // Narrowed rather than spread conditionally: `exactOptionalPropertyTypes`
  // draws a hard line between "absent" and "present but undefined", and Vivox
  // needs `t` genuinely absent on a login token.
  if (toUri === undefined || toUri === '') {
    throw new Error('A join token requires a channel URI.');
  }

  return { ...claims, t: toUri };
}

/**
 * Base64url: standard base64 with the URL-unsafe characters swapped and the
 * padding dropped. Vivox rejects a padded token.
 *
 * @param bytes - the buffer to encode
 * @returns the base64url text
 */
function base64Url(bytes: Buffer): string {
  return bytes.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * Signs a claim set into a Vivox access token.
 *
 * @param claims - the claims from {@link buildClaims}
 * @param key - the Vivox token-signing key, from Secret Manager
 * @returns the encoded, signed token
 */
export function signAccessToken(claims: VivoxClaims, key: string): string {
  const payload = base64Url(Buffer.from(JSON.stringify(claims), 'utf8'));
  const signingInput = `${ENCODED_HEADER}.${payload}`;
  const signature = base64Url(createHmac('sha256', key).update(signingInput, 'utf8').digest());

  return `${signingInput}.${signature}`;
}
