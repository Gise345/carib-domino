import { describe, expect, it } from 'vitest';
import { resultForSeat } from '../../src/settlement/roundResult';
import { MatchOutcome, Partnership } from '../../src/rules';

function outcome(partial: Partial<MatchOutcome>): MatchOutcome {
  return {
    reason: 'domino',
    winnerId: null,
    winningTeamId: null,
    winnerScore: 0,
    isKey: false,
    remainingPips: new Map(),
    ...partial,
  };
}

describe('resultForSeat — Cut-Throat (solo teams)', () => {
  const players = ['p0', 'p1', 'p2'];
  const partnership = Partnership.cutThroat(players);

  it('marks the winning seat won with the winner score', () => {
    const o = outcome({ winnerId: 'p1', winningTeamId: 'team:p1', winnerScore: 37 });
    expect(resultForSeat(o, players, 1, partnership)).toEqual({ result: 'won', score: 37 });
  });

  it('marks the other seats lost', () => {
    const o = outcome({ winnerId: 'p1', winningTeamId: 'team:p1', winnerScore: 37 });
    expect(resultForSeat(o, players, 0, partnership)).toEqual({ result: 'lost', score: 0 });
    expect(resultForSeat(o, players, 2, partnership)).toEqual({ result: 'lost', score: 0 });
  });

  it('marks every seat a draw when there is no winning team', () => {
    const o = outcome({ reason: 'blocked', winningTeamId: null });
    for (let i = 0; i < players.length; i++) {
      expect(resultForSeat(o, players, i, partnership)).toEqual({ result: 'draw', score: 0 });
    }
  });
});

describe('resultForSeat — Jamaican Partner (both partners share the result)', () => {
  const players = ['p0', 'p1', 'p2', 'p3'];
  const partnership = Partnership.alternatingPairs('p0', 'p1', 'p2', 'p3'); // team_a = p0,p2

  it('both members of the winning team won', () => {
    const o = outcome({ winnerId: 'p0', winningTeamId: 'team_a', winnerScore: 20 });
    expect(resultForSeat(o, players, 0, partnership)).toEqual({ result: 'won', score: 20 });
    expect(resultForSeat(o, players, 2, partnership)).toEqual({ result: 'won', score: 20 });
  });

  it('both members of the losing team lost', () => {
    const o = outcome({ winnerId: 'p0', winningTeamId: 'team_a', winnerScore: 20 });
    expect(resultForSeat(o, players, 1, partnership)).toEqual({ result: 'lost', score: 0 });
    expect(resultForSeat(o, players, 3, partnership)).toEqual({ result: 'lost', score: 0 });
  });
});
