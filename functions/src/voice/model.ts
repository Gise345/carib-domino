/**
 * Shared voice types + limits (ADR 0024). Pure — no Firestore, no Firebase
 * imports — so every limit is unit-testable and the callables and the tests read
 * the same numbers.
 */

/**
 * How long a minted Vivox access token stays valid. Vivox's own guidance is
 * `now + 90s`, and short is correct here: the token authorises a single ACTION
 * (log in, join this channel), not a session. Once the action is performed the
 * token is spent, so a 45-minute series never re-mints anything.
 */
export const VOICE_TOKEN_TTL_MS = 90_000;

/**
 * Token mints allowed per player inside {@link VOICE_TOKEN_WINDOW_MS}. A healthy
 * match needs two (login, then join) plus a few more across reconnects, so ten is
 * generous. The cap exists because every mint is a billed credential: without it a
 * looping client could churn them indefinitely.
 */
export const VOICE_TOKEN_RATE_MAX = 10;

/** Sliding window the token allowance is measured over. */
export const VOICE_TOKEN_WINDOW_MS = 60_000;

/**
 * The Vivox actions we will ever mint a token for. Deliberately NOT the full
 * Vivox set — `kick`, `mute` and `transcription` are moderator powers that would
 * let one player act on another, and nothing in this game should ask for them.
 */
export const VOICE_ACTIONS = ['login', 'join'] as const;

export type VoiceAction = (typeof VOICE_ACTIONS)[number];

/**
 * Whether a client-supplied action is one we mint.
 *
 * @param action - the candidate action
 * @returns true when the action is allowed
 */
export function isVoiceAction(action: string): action is VoiceAction {
  return (VOICE_ACTIONS as readonly string[]).includes(action);
}

/** Voice is switched off for this table, or the caller may not use it. */
export const REFUSAL_VOICE_DISABLED = 'voice-disabled';

/** The caller is not a member of the room they asked to speak in. */
export const REFUSAL_NOT_IN_ROOM = 'not-in-room';

/** Shape of a `/chatRooms/{roomId}` voice roster entry. */
export interface VoiceMember {
  /** When this player was admitted to the voice channel. */
  readonly joinedAt: Date;
  /** Whether they were entitled to transmit at join time. */
  readonly canSpeak: boolean;
}
