/**
 * Chat/voice entitlement (ADR 0023 §3). A GUEST — Firebase anonymous auth — may
 * read a room but may not send a message and may not use voice, because an
 * anonymous identity costs one tap to replace and so cannot be moderated.
 *
 * The decision reads the `sign_in_provider` claim out of the SIGNED token, which
 * a client cannot forge. The in-game lock on the composer is cosmetic; this is
 * the gate.
 */

import { CallableRequest, HttpsError } from 'firebase-functions/v2/https';
import { REFUSAL_GUEST, refusal } from './refusals';

/** Minimal shape of the decoded token bits we depend on. */
export interface SignInInfo {
  readonly firebase?: { readonly sign_in_provider?: string } | undefined;
}

/**
 * Whether the caller signed in anonymously.
 *
 * @param token - the decoded ID token from the callable request
 * @returns true for an anonymous (guest) session
 */
export function isGuestToken(token: SignInInfo | undefined): boolean {
  return token?.firebase?.sign_in_provider === 'anonymous';
}

/**
 * Guards a feature that requires a real account.
 *
 * @param request - the callable request
 * @throws HttpsError('unauthenticated') when signed out;
 *         HttpsError('permission-denied') with code `guest-restricted` for guests
 */
export function assertNotGuest(request: CallableRequest<unknown>): string {
  const uid = request.auth?.uid;
  if (uid === undefined) {
    throw new HttpsError('unauthenticated', 'Sign-in required.');
  }
  if (isGuestToken(request.auth?.token)) {
    throw new HttpsError(
      'permission-denied',
      refusal(REFUSAL_GUEST, 'Create a free account to use chat and voice.'),
      { code: REFUSAL_GUEST },
    );
  }
  return uid;
}
