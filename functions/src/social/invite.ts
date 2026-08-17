import { INVITE_DAILY_CAP, INVITE_REWARD } from '../lib/economy';

/**
 * Pure logic for the daily-capped Facebook invite reward (M7). Kept side-effect
 * free so the cap / de-dup rules are unit-tested in isolation and reused by the
 * `claimInviteReward` callable. See ADR 0017.
 *
 * A player earns {@link INVITE_REWARD} coins per invite sent, up to
 * {@link INVITE_DAILY_CAP} rewarded invites per (UTC) day. Each invite carries a
 * unique id so a replay of the same invite is never double-paid, and the per-day
 * record resets on a new day, bounding the stored id list to the daily cap.
 */

/** A player's rewarded-invites record for one day. */
export interface InviteDay {
  /** UTC date `YYYY-MM-DD` the ids belong to. */
  readonly day: string;
  /** Invite ids already rewarded today. */
  readonly ids: readonly string[];
}

/** Why an invite claim was or wasn't rewarded. */
export type InviteOutcome = 'ok' | 'duplicate' | 'cap';

export interface InviteResult {
  readonly reward: number;
  readonly outcome: InviteOutcome;
  readonly nextRecord: InviteDay;
  /** Rewarded invites still available today after this claim. */
  readonly remainingToday: number;
}

/**
 * Decides the reward for one invite claim.
 *
 * @param record - the player's stored record (any day), or null if none yet
 * @param today - the current UTC date `YYYY-MM-DD`
 * @param inviteId - unique id for this invite (client- or FB-generated)
 * @returns the reward (0 on duplicate/cap), the record to persist, and the outcome
 */
export function evaluateInvite(
  record: InviteDay | null,
  today: string,
  inviteId: string,
): InviteResult {
  // A record from an earlier day resets to a fresh, empty day.
  const dayIds: readonly string[] = record?.day === today ? record.ids : [];

  if (dayIds.includes(inviteId)) {
    return {
      reward: 0,
      outcome: 'duplicate',
      nextRecord: { day: today, ids: dayIds },
      remainingToday: Math.max(0, INVITE_DAILY_CAP - dayIds.length),
    };
  }
  if (dayIds.length >= INVITE_DAILY_CAP) {
    return {
      reward: 0,
      outcome: 'cap',
      nextRecord: { day: today, ids: dayIds },
      remainingToday: 0,
    };
  }
  const ids = [...dayIds, inviteId];
  return {
    reward: INVITE_REWARD,
    outcome: 'ok',
    nextRecord: { day: today, ids },
    remainingToday: Math.max(0, INVITE_DAILY_CAP - ids.length),
  };
}
