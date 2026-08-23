/**
 * Vivox SIP URI and channel-name construction (ADR 0024). Pure — no Firestore, no
 * secrets — because these strings are the part of token minting most likely to be
 * subtly wrong, and a wrong URI surfaces as an opaque Vivox authentication
 * failure with no useful error. Pinning them with tests is the only way to be
 * sure.
 */

import { createHash } from 'crypto';

/**
 * Characters Vivox permits in a channel or player name. Anything outside this set
 * is rejected by the service rather than escaped.
 */
const VIVOX_NAME_PATTERN = /^[0-9A-Za-z!()+\-.=_~]{1,200}$/;

/** Prefix on every channel we create, so Pose channels are identifiable. */
const CHANNEL_PREFIX = 'pose-';

/** Hex characters of the room hash kept in the channel name. */
const CHANNEL_HASH_LENGTH = 32;

/**
 * Whether a name is legal for Vivox.
 *
 * @param name - the candidate channel or player name
 * @returns true when Vivox will accept it
 */
export function isValidVivoxName(name: string): boolean {
  return VIVOX_NAME_PATTERN.test(name);
}

/**
 * The Vivox channel name for a room.
 *
 * Hashed rather than used verbatim, for two reasons. Vivox folds case-variant
 * channel names together but Photon session names are case-SENSITIVE, so the
 * genuinely distinct rooms `ABC123` and `abc123` would otherwise share one voice
 * channel and put strangers in each other's ears. And a raw room code on Vivox's
 * wire is a guessable handle to a private table.
 *
 * Deterministic, so every player in a room derives the same channel without
 * coordinating, and `voiceChannel` is stored on the room document so a moderator
 * can still go report → roomId → channel in one hop.
 *
 * @param roomId - the Photon session name / chat room id
 * @returns a Vivox-legal channel name
 */
export function voiceChannelName(roomId: string): string {
  const digest = createHash('sha256').update(roomId, 'utf8').digest('hex');
  return `${CHANNEL_PREFIX}${digest.slice(0, CHANNEL_HASH_LENGTH)}`;
}

/**
 * The SIP URI identifying a player to Vivox.
 *
 * The leading dot and the dot before `@` are not typos — Vivox's parser requires
 * this exact shape to extract the player id, and Unity's moderation tooling
 * depends on it being right.
 *
 * @param issuer - the Vivox issuer for this environment
 * @param domain - the Vivox domain, e.g. `tla.vivox.com`
 * @param playerId - the player's stable id (here, their Firebase uid)
 * @returns the player's SIP URI
 */
export function userUri(issuer: string, domain: string, playerId: string): string {
  return `sip:.${issuer}.${playerId}.@${domain}`;
}

/**
 * The SIP URI identifying a channel to Vivox.
 *
 * `confctl-g-` marks a non-positional (2D) group channel, which is what a table
 * of four players talking wants — positional audio would attenuate players by a
 * spatial distance that has no meaning on a domino board.
 *
 * @param issuer - the Vivox issuer for this environment
 * @param domain - the Vivox domain, e.g. `tla.vivox.com`
 * @param channelName - the channel name from {@link voiceChannelName}
 * @returns the channel's SIP URI
 */
export function channelUri(issuer: string, domain: string, channelName: string): string {
  return `sip:confctl-g-${issuer}.${channelName}@${domain}`;
}
