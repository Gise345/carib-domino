import { MatchOutcome, PlayerId } from '../rules';

/** A single seat's result, derived from the server-recomputed outcome. */
export interface SeatResult {
  readonly result: 'won' | 'lost' | 'draw';
  readonly score: number;
}

/**
 * Resolves what the player at `seatIndex` earned this round from the
 * authoritative outcome. A draw (tied block) is a draw for everyone; otherwise
 * the seat matching the winner won and the rest lost. Score is the winner's pip
 * haul; 0 for losers and draws — the same shape the old client-trusting path
 * wrote, but now computed server-side from a replayed game.
 *
 * Cut-Throat only for now (solo teams, so winner identity == winning seat). Team
 * variants will resolve via the winning team id.
 */
export function resultForSeat(
  outcome: MatchOutcome,
  players: readonly PlayerId[],
  seatIndex: number,
): SeatResult {
  const player = players[seatIndex];
  if (player === undefined) {
    throw new RangeError(`Seat ${String(seatIndex)} is out of range.`);
  }

  if (outcome.winnerId === null) {
    return { result: 'draw', score: 0 };
  }
  if (outcome.winnerId === player) {
    return { result: 'won', score: outcome.winnerScore };
  }
  return { result: 'lost', score: 0 };
}
