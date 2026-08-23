/**
 * The trust decision behind {@link mintVivoxToken} (ADR 0024 §4), extracted pure
 * so it can be exhaustively tested without Firestore.
 *
 * WHY THIS FILE EXISTS AT ALL. Vivox's `IVivoxTokenProvider` hands the CLIENT's
 * `fromUserUri` and `channelUri` to the server and asks it to sign them. Signing
 * what was asked for would turn the callable into an oracle that mints a token to
 * join any channel as any user — a total bypass of the entitlement model ADR 0023
 * built. So this function does not merely ignore those arguments: it never
 * accepts them. The only identity it will vouch for is the one on the verified
 * token, and the only channel it will name is the one stored on the room the
 * caller is a proven member of.
 *
 * If you are tempted to add a `fromUri` or `channelUri` parameter here, don't.
 */

import { REFUSAL_NOT_IN_ROOM, VoiceAction } from './model';
import { isValidRoomId } from '../chat/model';

/** What the caller is asking to do, and what the server knows about the room. */
export interface AuthorizationInput {
  /** The action requested. */
  readonly action: VoiceAction;
  /** The caller's uid, taken from the SIGNED token — never from the payload. */
  readonly callerUid: string;
  /** The room, for a `join`. Absent for a `login`. */
  readonly roomId?: string | undefined;
  /** Whether that room document exists. */
  readonly roomExists?: boolean | undefined;
  /** The room's membership map, keyed by uid. */
  readonly roomMembers?: Readonly<Record<string, unknown>> | undefined;
  /** The channel name stored on the room by `joinVoiceRoom`. */
  readonly roomVoiceChannel?: string | undefined;
}

/** A granted request, carrying only server-derived values. */
export interface AuthorizationGranted {
  readonly ok: true;
  /** The player id to put in the `f` claim. Always the caller's own uid. */
  readonly fromPlayerId: string;
  /** The channel for the `t` claim, or null for a login token. */
  readonly channelName: string | null;
}

/** A refused request, with a code the client can act on. */
export interface AuthorizationRefused {
  readonly ok: false;
  readonly code: string;
  readonly message: string;
}

export type Authorization = AuthorizationGranted | AuthorizationRefused;

/**
 * Decides whether a token may be minted, and for what.
 *
 * @param input - the request and the server's view of the room
 * @returns the granted values, or a refusal
 */
export function authorizeTokenRequest(input: AuthorizationInput): Authorization {
  const { action, callerUid, roomId, roomExists, roomMembers, roomVoiceChannel } = input;

  if (callerUid === '') {
    return { ok: false, code: REFUSAL_NOT_IN_ROOM, message: 'Sign-in required.' };
  }

  // A login token names no destination, so there is nothing further to check.
  if (action === 'login') {
    return { ok: true, fromPlayerId: callerUid, channelName: null };
  }

  if (roomId === undefined || !isValidRoomId(roomId)) {
    return { ok: false, code: REFUSAL_NOT_IN_ROOM, message: 'A join token needs a valid room.' };
  }

  if (roomExists !== true) {
    return { ok: false, code: REFUSAL_NOT_IN_ROOM, message: 'That room does not exist.' };
  }

  // Membership is the whole gate: it is written by each caller for their OWN uid
  // (ADR 0023 §2), so it cannot be forged by a host supplying a roster.
  const members = roomMembers ?? {};
  if (!Object.prototype.hasOwnProperty.call(members, callerUid)) {
    return { ok: false, code: REFUSAL_NOT_IN_ROOM, message: 'You are not in that room.' };
  }

  // The channel comes from the room document, never from the request. A room
  // without one has not been through joinVoiceRoom, so voice is not open there.
  if (roomVoiceChannel === undefined || roomVoiceChannel === '') {
    return { ok: false, code: REFUSAL_NOT_IN_ROOM, message: 'Voice is not open in that room.' };
  }

  return { ok: true, fromPlayerId: callerUid, channelName: roomVoiceChannel };
}
