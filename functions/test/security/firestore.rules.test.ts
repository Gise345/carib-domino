import fs from 'node:fs';
import path from 'node:path';
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import {
  RulesTestEnvironment,
  assertFails,
  assertSucceeds,
  initializeTestEnvironment,
} from '@firebase/rules-unit-testing';
import {
  collection,
  deleteDoc,
  doc,
  getDoc,
  getDocs,
  limit,
  orderBy,
  query,
  setDoc,
  updateDoc,
} from 'firebase/firestore';

/**
 * Behavioural tests for `firestore.rules`, run against the Firestore emulator
 * (`npm run test:rules`).
 *
 * These assert the boundary itself rather than the code in front of it: the unit
 * suites prove `sendChatMessage` refuses a guest, and this proves that a client
 * which skips the callable entirely still cannot write a message, read a room it
 * is not in, or reach the verbatim text behind a mask. Rules are the only part of
 * the trust model an attacker talks to directly, so they are worth testing
 * directly. See ADR 0023 and ADR 0022.
 */

const RULES = fs.readFileSync(path.resolve(__dirname, '../../../firestore.rules'), 'utf8');

const ROOM = 'ROOMAB';
const ALICE = 'uid-alice';
const BOB = 'uid-bob';
const STRANGER = 'uid-stranger';

let env: RulesTestEnvironment;

beforeAll(async () => {
  const host = process.env['FIRESTORE_EMULATOR_HOST'] ?? '127.0.0.1:8080';
  const [hostname, port] = host.split(':');

  env = await initializeTestEnvironment({
    projectId: 'carib-domino-rules-test',
    firestore: {
      rules: RULES,
      host: hostname ?? '127.0.0.1',
      port: Number(port ?? 8080),
    },
  });
});

afterEach(async () => {
  await env.clearFirestore();
});

afterAll(async () => {
  await env.cleanup();
});

/** Seeds a room with Alice and Bob in it, one message, and its verbatim copy. */
async function seedRoom(): Promise<void> {
  await env.withSecurityRulesDisabled(async (context) => {
    const db = context.firestore();
    await setDoc(doc(db, 'chatRooms', ROOM), {
      members: {
        [ALICE]: { name: 'Alice', seat: 0 },
        [BOB]: { name: 'Bob', seat: 1 },
      },
      mode: 'cutthroat',
      matchIds: ['match-1'],
      retained: false,
    });
    await setDoc(doc(db, 'chatRooms', ROOM, 'messages', 'msg1'), {
      senderUid: BOB,
      senderName: 'Bob',
      seat: 1,
      text: 'you **** better watch it',
      filtered: true,
      severe: false,
      redacted: false,
      createdAt: new Date(),
    });
    await setDoc(doc(db, 'chatRooms', ROOM, 'originals', 'msg1'), {
      senderUid: BOB,
      originalText: 'you had better watch it',
    });
  });
}

describe('/chatRooms/{roomId}', () => {
  it('lets a member read the room', async () => {
    await seedRoom();
    const db = env.authenticatedContext(ALICE).firestore();

    await assertSucceeds(getDoc(doc(db, 'chatRooms', ROOM)));
  });

  it('refuses someone who is not in the room', async () => {
    await seedRoom();
    const db = env.authenticatedContext(STRANGER).firestore();

    await assertFails(getDoc(doc(db, 'chatRooms', ROOM)));
  });

  it('refuses a signed-out reader', async () => {
    await seedRoom();
    const db = env.unauthenticatedContext().firestore();

    await assertFails(getDoc(doc(db, 'chatRooms', ROOM)));
  });

  it('refuses every client write, including a member adding themselves', async () => {
    await seedRoom();
    const db = env.authenticatedContext(ALICE).firestore();

    await assertFails(updateDoc(doc(db, 'chatRooms', ROOM), { mode: 'partner' }));
    await assertFails(deleteDoc(doc(db, 'chatRooms', ROOM)));
    // The membership map is what the read rule trusts, so writing it must be
    // impossible from a client — otherwise anyone could enrol themselves.
    await assertFails(
      setDoc(doc(db, 'chatRooms', 'OTHERRM'), {
        members: { [ALICE]: { name: 'Alice', seat: 0 } },
      }),
    );
  });
});

describe('/chatRooms/{roomId}/messages', () => {
  it('lets a member read and list the conversation', async () => {
    await seedRoom();
    const db = env.authenticatedContext(ALICE).firestore();

    await assertSucceeds(getDoc(doc(db, 'chatRooms', ROOM, 'messages', 'msg1')));
    // The client subscribes with exactly this query.
    await assertSucceeds(
      getDocs(
        query(
          collection(db, 'chatRooms', ROOM, 'messages'),
          orderBy('createdAt', 'desc'),
          limit(100),
        ),
      ),
    );
  });

  it('refuses a non-member reading the conversation', async () => {
    await seedRoom();
    const db = env.authenticatedContext(STRANGER).firestore();

    await assertFails(getDoc(doc(db, 'chatRooms', ROOM, 'messages', 'msg1')));
    await assertFails(getDocs(collection(db, 'chatRooms', ROOM, 'messages')));
  });

  it('refuses a member posting without the callable', async () => {
    await seedRoom();
    const db = env.authenticatedContext(ALICE).firestore();

    // This is the whole point of the write path: no ban, guest, mute, rate-limit
    // or profanity gate can be skipped, because the client cannot write at all.
    await assertFails(
      setDoc(doc(db, 'chatRooms', ROOM, 'messages', 'forged'), {
        senderUid: ALICE,
        senderName: 'Alice',
        text: 'straight to the database',
        createdAt: new Date(),
      }),
    );
  });

  it('refuses a member forging a message from someone else', async () => {
    await seedRoom();
    const db = env.authenticatedContext(ALICE).firestore();

    await assertFails(
      setDoc(doc(db, 'chatRooms', ROOM, 'messages', 'forged2'), {
        senderUid: BOB,
        senderName: 'Bob',
        text: 'something Bob never said',
        createdAt: new Date(),
      }),
    );
  });

  it('refuses editing or deleting a delivered message', async () => {
    await seedRoom();
    const db = env.authenticatedContext(BOB).firestore();

    // Not even the sender: a message someone may be about to report must not be
    // editable out from under them.
    await assertFails(
      updateDoc(doc(db, 'chatRooms', ROOM, 'messages', 'msg1'), { text: 'nothing to see' }),
    );
    await assertFails(deleteDoc(doc(db, 'chatRooms', ROOM, 'messages', 'msg1')));
  });
});

describe('/chatRooms/{roomId}/originals', () => {
  it('hides the verbatim text from members', async () => {
    await seedRoom();
    const db = env.authenticatedContext(ALICE).firestore();

    // Rules are document-level, so the unmasked text lives in its own document
    // rather than as a field on the readable one. This is that guarantee.
    await assertFails(getDoc(doc(db, 'chatRooms', ROOM, 'originals', 'msg1')));
    await assertFails(getDocs(collection(db, 'chatRooms', ROOM, 'originals')));
  });

  it('hides it from the sender too', async () => {
    await seedRoom();
    const db = env.authenticatedContext(BOB).firestore();

    await assertFails(getDoc(doc(db, 'chatRooms', ROOM, 'originals', 'msg1')));
    await assertFails(
      setDoc(doc(db, 'chatRooms', ROOM, 'originals', 'msg1'), { originalText: 'rewritten' }),
    );
  });
});

describe('moderation collections', () => {
  it('keeps reports off the device entirely', async () => {
    await env.withSecurityRulesDisabled(async (context) => {
      await setDoc(doc(context.firestore(), 'chatReports', 'r1'), {
        reporterUid: ALICE,
        reportedUid: BOB,
        transcript: [],
      });
    });
    const db = env.authenticatedContext(ALICE).firestore();

    // Not even the reporter: a report carries other players' messages.
    await assertFails(getDoc(doc(db, 'chatReports', 'r1')));
    await assertFails(setDoc(doc(db, 'chatReports', 'r2'), { reporterUid: ALICE }));
  });

  it('keeps a mute unreadable and unwritable by the muted player', async () => {
    await env.withSecurityRulesDisabled(async (context) => {
      await setDoc(doc(context.firestore(), 'chatMutes', BOB), { until: new Date() });
    });
    const db = env.authenticatedContext(BOB).firestore();

    await assertFails(getDoc(doc(db, 'chatMutes', BOB)));
    await assertFails(deleteDoc(doc(db, 'chatMutes', BOB)));
  });

  it('keeps the rate-limit window out of reach of the sender', async () => {
    await env.withSecurityRulesDisabled(async (context) => {
      await setDoc(doc(context.firestore(), 'chatRateLimits', ALICE), { window: [1, 2, 3] });
    });
    const db = env.authenticatedContext(ALICE).firestore();

    await assertFails(getDoc(doc(db, 'chatRateLimits', ALICE)));
    await assertFails(setDoc(doc(db, 'chatRateLimits', ALICE), { window: [] }));
  });

  it('keeps bans and the audit log server-only', async () => {
    const db = env.authenticatedContext(ALICE).firestore();

    await assertFails(getDoc(doc(db, 'bans', ALICE)));
    await assertFails(deleteDoc(doc(db, 'bans', ALICE)));
    await assertFails(getDoc(doc(db, 'adminAudit', 'entry')));
    await assertFails(setDoc(doc(db, 'adminAudit', 'forged'), { action: 'unban_user' }));
  });
});

describe('the surrounding trust model', () => {
  it('lets a player own their profile and no one else theirs', async () => {
    const alice = env.authenticatedContext(ALICE).firestore();

    await assertSucceeds(setDoc(doc(alice, 'users', ALICE), { displayName: 'Alice' }));
    await assertSucceeds(getDoc(doc(alice, 'users', ALICE)));
    await assertFails(getDoc(doc(alice, 'users', BOB)));
    await assertFails(setDoc(doc(alice, 'users', BOB), { displayName: 'not Bob' }));
  });

  it('lets a player read their wallet but never write it', async () => {
    await env.withSecurityRulesDisabled(async (context) => {
      await setDoc(doc(context.firestore(), 'wallets', ALICE), { coins: 10_000 });
    });
    const alice = env.authenticatedContext(ALICE).firestore();

    await assertSucceeds(getDoc(doc(alice, 'wallets', ALICE)));
    await assertFails(updateDoc(doc(alice, 'wallets', ALICE), { coins: 999_999 }));
    await assertFails(getDoc(doc(alice, 'wallets', BOB)));
  });

  it('lets a player read their stats but never write them', async () => {
    await env.withSecurityRulesDisabled(async (context) => {
      await setDoc(doc(context.firestore(), 'stats', ALICE), { wins: 3 });
    });
    const alice = env.authenticatedContext(ALICE).firestore();

    await assertSucceeds(getDoc(doc(alice, 'stats', ALICE)));
    await assertFails(updateDoc(doc(alice, 'stats', ALICE), { wins: 300 }));
  });

  it('denies a collection nobody has written a rule for', async () => {
    const alice = env.authenticatedContext(ALICE).firestore();

    await assertFails(getDoc(doc(alice, 'somethingNew', 'x')));
    await assertFails(setDoc(doc(alice, 'somethingNew', 'x'), { any: 'thing' }));
  });
});
