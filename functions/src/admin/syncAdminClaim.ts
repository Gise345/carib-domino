import { onCall, CallableRequest, HttpsError } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';
import { isAllowlistedAdmin } from './admins';

if (getApps().length === 0) {
  initializeApp();
}

/**
 * Grants — or self-heals/revokes — the caller's OWN `admin` custom claim based on
 * the server-side allowlist. The email is read from the verified, Google-signed
 * token (`email` + `email_verified`), so it cannot be spoofed, and the allowlist
 * ({@link isAllowlistedAdmin}) lives only in server code. A non-allowlisted caller
 * gets nothing, and a stale `admin` claim on a now-removed email is stripped on
 * the next call.
 *
 * The admin dashboard calls this right after Google sign-in, then force-refreshes
 * its ID token so the claim takes effect. Custom claims are what Firestore rules
 * and admin callables gate on. See ADR 0022.
 *
 * @returns `{ admin }` — whether the caller now holds admin.
 */
export const syncAdminClaim = onCall(
  async (request: CallableRequest<unknown>): Promise<{ admin: boolean }> => {
    const uid = request.auth?.uid;
    const token = request.auth?.token;
    if (uid === undefined || token === undefined) {
      throw new HttpsError('unauthenticated', 'Sign-in required.');
    }

    const email: unknown = token.email;
    const emailStr = typeof email === 'string' ? email : '';
    const shouldBeAdmin = token.email_verified === true && isAllowlistedAdmin(emailStr);
    const adminClaim: unknown = token['admin'];
    const currentlyAdmin = adminClaim === true;

    if (shouldBeAdmin !== currentlyAdmin) {
      await getAuth().setCustomUserClaims(uid, shouldBeAdmin ? { admin: true } : {});
      logger.info('syncAdminClaim', { uid, email: emailStr, admin: shouldBeAdmin });
    }
    return { admin: shouldBeAdmin };
  },
);
