import { MatchOutcome, Partnership, PlayerId } from '../rules';

/** A single seat's result, derived from the server-recomputed outcome. */
export interface SeatResult {
  readonly result: 'won' | 'lost' | 'draw';
  readonly score: number;
}

/**
 * Resolves what the player at `seatIndex` earned this round from the
 * authoritative outcome, by TEAM. A draw is a draw for everyone; otherwise every
 * seat whose team is the winning team won (both partners in Jamaican Partner),
 * and the rest lost. Score is the winner's pip haul; 0 for losers and draws.
 *
 * Team-based comparison generalises cleanly: for Cut-Throat, each seat is its own
 * solo team, so this reduces to "did this seat win".
 */
export function resultForSeat(
  outcome: MatchOutcome,
  players: readonly PlayerId[],
  seatIndex: number,
  partnership: Partnership,
): SeatResult {
  const player = players[seatIndex];
  if (player === undefined) {
    throw new RangeError(`Seat ${String(seatIndex)} is out of range.`);
  }

  if (outcome.winningTeamId === null) {
    return { result: 'draw', score: 0 };
  }
  if (partnership.getTeamOf(player) === outcome.winningTeamId) {
    return { result: 'won', score: outcome.winnerScore };
  }
  return { result: 'lost', score: 0 };
}
