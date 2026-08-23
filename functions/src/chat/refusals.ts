/**
 * Machine-readable refusal codes for chat (ADR 0023).
 *
 * The Unity Firebase SDK's `FunctionsException` exposes only `ErrorCode` and
 * `Message` — the callable's structured `details` payload never reaches the
 * game client. So the code a client must act on is carried as a stable prefix on
 * the message itself: `"muted: You are muted in chat."`. The client parses the
 * prefix and shows its own localised string; the human half is for logs and the
 * web SDK, never for the player.
 *
 * `Pose.Core.Chat.ChatRefusal` is the parsing half of this contract.
 */

/** A guest tried to send. They need a real account.  */
export const REFUSAL_GUEST = 'guest-restricted';

/** A moderator mute is in force. */
export const REFUSAL_MUTED = 'muted';

/** The sender is over the rate limit. */
export const REFUSAL_RATE_LIMITED = 'rate-limited';

/**
 * Builds a refusal message carrying its code.
 *
 * @param code - one of the REFUSAL_* constants
 * @param human - the readable explanation, for logs and non-game clients
 * @returns the prefixed message
 */
export function refusal(code: string, human: string): string {
  return `${code}: ${human}`;
}
