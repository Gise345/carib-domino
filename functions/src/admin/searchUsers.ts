import { onCall, HttpsError, CallableRequest } from 'firebase-functions/v2/https';
import { getApps, initializeApp } from 'firebase-admin/app';
import { getFirestore, DocumentData } from 'firebase-admin/firestore';
import { getAuth } from 'firebase-admin/auth';
import { z } from 'zod';
import { assertAdmin } from './requireAdmin';
import { looksLikeEmail } from './searchQuery';

if (getApps().length === 0) {
  initializeApp();
}

// High-codepoint sentinel (U+F8FF) for a Firestore display-name prefix range.
const PREFIX_END = String.fromCharCode(0xf8ff);

const SearchSchema = z.object({ query: z.string().trim().min(1).max(200) });

interface UserRow {
  uid: string;
  name: string;
  photoURL: string;
}

function rowFrom(uid: string, data: DocumentData | undefined): UserRow {
  const dn: unknown = data?.['displayName'];
  const photo: unknown = data?.['photoURL'];
  return {
    uid,
    name: typeof dn === 'string' && dn.length > 0 ? dn : 'Player',
    photoURL: typeof photo === 'string' ? photo : '',
  };
}

/**
 * Admin user search (ADR 0022, phase C). An `@`-query is an exact email lookup via
 * Auth; otherwise it matches an exact uid and a case-sensitive display-name prefix
 * (capped at 20). Admin-gated, read-only. See ADR 0022.
 *
 * @returns `{ users }` — matched users (uid + name + avatar), de-duplicated.
 */
export const searchUsers = onCall(
  async (request: CallableRequest<unknown>): Promise<{ users: UserRow[] }> => {
    assertAdmin(request);
    const parsed = SearchSchema.safeParse(request.data);
    if (!parsed.success) {
      throw new HttpsError('invalid-argument', 'Invalid search query.');
    }
    const q = parsed.data.query;
    const db = getFirestore();
    const results = new Map<string, UserRow>();

    if (looksLikeEmail(q)) {
      try {
        const record = await getAuth().getUserByEmail(q);
        const snap = await db.collection('users').doc(record.uid).get();
        results.set(record.uid, rowFrom(record.uid, snap.data()));
      } catch {
        // No user with that email — leave results empty.
      }
    } else {
      const byId = await db.collection('users').doc(q).get();
      if (byId.exists) {
        results.set(byId.id, rowFrom(byId.id, byId.data()));
      }
      const prefix = await db
        .collection('users')
        .orderBy('displayName')
        .startAt(q)
        .endAt(q + PREFIX_END)
        .limit(20)
        .get();
      for (const d of prefix.docs) {
        results.set(d.id, rowFrom(d.id, d.data()));
      }
    }

    return { users: Array.from(results.values()) };
  },
);
