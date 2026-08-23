import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { AdminContext } from './requireAdmin';

/**
 * Appends an immutable admin-action record to `/adminAudit` (server-only, no
 * client access). Every admin mutation — claim change, ban, promo — records who
 * did what to whom and when, for accountability and forensics. See ADR 0022.
 *
 * @param actor - the verified admin performing the action
 * @param action - a short verb, e.g. "ban_user" / "create_promotion"
 * @param target - the id the action affects (a uid, promo id, etc.)
 * @param details - any extra structured context
 */
export async function writeAudit(
  actor: AdminContext,
  action: string,
  target: string,
  details: Record<string, unknown> = {},
): Promise<void> {
  const db = getFirestore();
  await db.collection('adminAudit').add({
    action,
    actorUid: actor.uid,
    actorEmail: actor.email,
    target,
    details,
    at: FieldValue.serverTimestamp(),
  });
}
