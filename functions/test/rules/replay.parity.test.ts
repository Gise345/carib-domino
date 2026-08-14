import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { GameMode } from '../../src/rules/gameMode';
import { replayRound, ReplayMove } from '../../src/rules/replay';

interface ReplayFixture {
  seed: string;
  mode: GameMode;
  players: string[];
  moves: ReplayMove[];
  openerIndex?: number;
  freeOpening?: boolean;
  expected: {
    reason: string;
    winnerIndex: number;
    winningTeamId: string;
    winnerScore: number;
    isKey: boolean;
    remainingPips: number[];
  };
}

const here = dirname(fileURLToPath(import.meta.url));
const fixtures = JSON.parse(
  readFileSync(join(here, '../fixtures/replay-fixtures.json'), 'utf8'),
) as { replays: ReplayFixture[] };

describe('replayRound parity with the C# engine', () => {
  it('has fixtures covering every ending type and both modes', () => {
    const reasons = new Set(fixtures.replays.map((f) => f.expected.reason));
    expect(reasons).toContain('domino');
    expect(reasons).toContain('blocked');
    expect(reasons).toContain('resigned');
    expect(fixtures.replays.some((f) => f.expected.winnerIndex === -1)).toBe(true);

    const modes = new Set(fixtures.replays.map((f) => f.mode));
    expect(modes).toContain('cutthroat');
    expect(modes).toContain('partner');
    // Partner outcomes must land on real team ids.
    expect(
      fixtures.replays.some((f) => f.mode === 'partner' && f.expected.winningTeamId === 'team_a'),
    ).toBe(true);
    expect(
      fixtures.replays.some((f) => f.mode === 'partner' && f.expected.winningTeamId === 'team_b'),
    ).toBe(true);
  });

  for (let i = 0; i < 9999; i++) {
    const fx = fixtures.replays[i];
    if (fx === undefined) {
      break;
    }
    const label = `${fx.mode} ${fx.players.length}P seed ${fx.seed} (${fx.expected.reason})`;
    it(`reproduces ${label}`, () => {
      const outcome = replayRound({
        seed: fx.seed,
        mode: fx.mode,
        players: fx.players,
        moves: fx.moves,
        openerIndex: fx.openerIndex ?? -1,
        freeOpening: fx.freeOpening ?? false,
      });

      expect(outcome.reason).toBe(fx.expected.reason);

      const expectedWinner =
        fx.expected.winnerIndex === -1 ? null : fx.players[fx.expected.winnerIndex];
      expect(outcome.winnerId).toBe(expectedWinner);

      const expectedTeam = fx.expected.winningTeamId === '' ? null : fx.expected.winningTeamId;
      expect(outcome.winningTeamId).toBe(expectedTeam);

      expect(outcome.winnerScore).toBe(fx.expected.winnerScore);

      expect(outcome.isKey).toBe(fx.expected.isKey);

      for (let p = 0; p < fx.players.length; p++) {
        expect(outcome.remainingPips.get(fx.players[p]!)).toBe(fx.expected.remainingPips[p]);
      }
    });
  }
});
