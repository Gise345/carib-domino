import { describe, expect, it } from 'vitest';
import {
  KEY_POINTS,
  POINTS_PER_WIN,
  accumulate,
  seriesTarget,
  seriesWinner,
} from '../../src/economy/series';

describe('seriesTarget', () => {
  it('is 6000 for classic and 3000 for quick', () => {
    expect(seriesTarget('classic')).toBe(6_000);
    expect(seriesTarget('quick')).toBe(3_000);
  });
});

describe('accumulate', () => {
  it('adds the flat win to the winning team and leaves the input unmutated', () => {
    const start = Object.freeze({ team_a: 1_000 });
    const next = accumulate(start, 'team_a', false);
    expect(next['team_a']).toBe(1_000 + POINTS_PER_WIN);
    expect(start['team_a']).toBe(1_000); // not mutated
  });

  it('adds the key bonus instead of the flat win', () => {
    expect(accumulate({}, 'team_b', true)['team_b']).toBe(KEY_POINTS);
  });

  it('scores nothing on a draw', () => {
    expect(accumulate({ team_a: 2_000 }, null, false)).toEqual({ team_a: 2_000 });
  });
});

describe('seriesWinner', () => {
  it('is null while every team is below the target', () => {
    expect(seriesWinner({ team_a: 5_000, team_b: 2_000 }, 'classic')).toBeNull();
  });

  it('is the team that reached the classic target', () => {
    expect(seriesWinner({ team_a: 6_000, team_b: 3_000 }, 'classic')).toBe('team_a');
  });

  it('respects the quick target', () => {
    expect(seriesWinner({ team_a: 3_000, team_b: 1_000 }, 'quick')).toBe('team_a');
    expect(seriesWinner({ team_a: 2_000, team_b: 1_000 }, 'quick')).toBeNull();
  });

  it('reflects a full Cut-Throat race to 6000', () => {
    let pts: Record<string, number> = {};
    for (let i = 0; i < 6; i++) {
      pts = accumulate(pts, 'p1', false);
    }
    expect(seriesWinner(pts, 'classic')).toBe('p1');
  });
});
