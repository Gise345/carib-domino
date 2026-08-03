import { describe, expect, it } from 'vitest';
import { resultForSeat } from '../../src/settlement/roundResult';
import { MatchOutcome } from '../../src/rules';

function outcome(partial: Partial<MatchOutcome>): MatchOutcome {
  return {
    reason: 'domino',
    winnerId: null,
    winningTeamId: null,
    winnerScore: 0,
    remainingPips: new Map(),
    ...partial,
  };
}

const players = ['p0', 'p1', 'p2'];

describe('resultForSeat', () => {
  it('marks the winning seat as won with the winner score', () => {
    const o = outcome({ winnerId: 'p1', winnerScore: 37 });

    expect(resultForSeat(o, players, 1)).toEqual({ result: 'won', score: 37 });
  });

  it('marks non-winning seats as lost with zero score', () => {
    const o = outcome({ winnerId: 'p1', winnerScore: 37 });

    expect(resultForSeat(o, players, 0)).toEqual({ result: 'lost', score: 0 });
    expect(resultForSeat(o, players, 2)).toEqual({ result: 'lost', score: 0 });
  });

  it('marks every seat a draw when there is no winner', () => {
    const o = outcome({ reason: 'blocked', winnerId: null, winnerScore: 0 });

    for (let i = 0; i < players.length; i++) {
      expect(resultForSeat(o, players, i)).toEqual({ result: 'draw', score: 0 });
    }
  });

  it('throws for an out-of-range seat', () => {
    expect(() => resultForSeat(outcome({}), players, 9)).toThrow();
  });
});
