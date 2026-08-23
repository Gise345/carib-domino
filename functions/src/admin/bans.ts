import { getFirestore } from 'firebase-admin/firestore';
import { HttpsError } from 'firebase-functions/v2/https';

/**
 * Throws if the uid is banned (a `/bans/{uid}` doc exists). This is the
 * authoritative, immediate ban gate for gameplay/economy entrypoints — a ban
 * blocks the next call regardless of token/claim propagation. Call it right after
 * the auth check in each protected function. See ADR 0022.
 *
 * @param uid - the caller's uid
 * @throws HttpsError('permission-denied') if the account is suspended
 */
export async function assertNotBanned(uid: string): Promise<void> {
  const snap = await getFirestore().collection('bans').doc(uid).get();
  if (snap.exists) {
    throw new HttpsError('permission-denied', 'This account is suspended.');
  }
}
