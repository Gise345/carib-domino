import { PlayerId, TeamId } from './ids';

/** Why a round ended. Port of `Pose.Core.MatchEndReason`. */
export type MatchEndReason = 'domino' | 'blocked' | 'resigned';

/**
 * The result of a finished round. Port of `Pose.Core.MatchOutcome`.
 * `winnerId` / `winningTeamId` are null only on a draw (a tied block).
 * `winnerScore` is the pips summed across every losing hand (0 on a draw).
 * `remainingPips` maps each player to their leftover pip total.
 */
export interface MatchOutcome {
  readonly reason: MatchEndReason;
  readonly winnerId: PlayerId | null;
  readonly winningTeamId: TeamId | null;
  readonly winnerScore: number;
  /**
   * True when the win is a "key" — a both-ends lock-out (capicúa with no
   * opponent holding either end). Worth the key bonus in a series; false for
   * blocks, resigns and ordinary domino wins. Port of `Pose.Core.MatchOutcome.IsKey`.
   */
  readonly isKey: boolean;
  readonly remainingPips: ReadonlyMap<PlayerId, number>;
}

export function isDraw(outcome: MatchOutcome): boolean {
  return outcome.winnerId === null;
}
