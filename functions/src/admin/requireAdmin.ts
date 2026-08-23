import { CallableRequest, HttpsError } from 'firebase-functions/v2/https';
import { isAllowlistedAdmin } from './admins';

/** The verified admin actor, for audit logging. */
export interface AdminContext {
  readonly uid: string;
  readonly email: string;
}

/**
 * Guards an admin callable. Requires ALL of: a signed-in user, a verified email,
 * the `admin` custom claim, and current membership in the server-side allowlist.
 * The allowlist re-check is defence in depth — a stale or forged claim is
 * rejected unless the email is still allowlisted. Returns the actor so the caller
 * can write an audit record. See ADR 0022.
 *
 * @param request - the callable request
 * @returns the verified admin actor (uid + email)
 * @throws HttpsError('unauthenticated') if not signed in;
 *         HttpsError('permission-denied') if not a current admin
 */
export function assertAdmin(request: CallableRequest<unknown>): AdminContext {
  const uid = request.auth?.uid;
  const token = request.auth?.token;
  if (uid === undefined || token === undefined) {
    throw new HttpsError('unauthenticated', 'Sign-in required.');
  }

  const email: unknown = token.email;
  const emailVerified = token.email_verified === true;
  const adminClaim: unknown = token['admin'];
  const hasClaim = adminClaim === true;
  const emailStr = typeof email === 'string' ? email : '';

  if (!hasClaim || !emailVerified || !isAllowlistedAdmin(emailStr)) {
    throw new HttpsError('permission-denied', 'Admin access required.');
  }
  return { uid, email: emailStr };
}
