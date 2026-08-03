import { describe, expect, it } from 'vitest';
import { cutThroatDoubleSix } from '../../src/rules/dealConfig';
import { CutThroatRules } from '../../src/rules/cutThroatRules';
import { deal } from '../../src/rules/dealer';
import { passMove, placeMove, resignMove } from '../../src/rules/move';
import { Partnership } from '../../src/rules/partnership';
import { SeededRandomSource } from '../../src/rules/prng';
import { replayRound } from '../../src/rules/replay';
import { Tile } from '../../src/rules/tile';

/**
 * Hand-asserted checks independent of the generated fixtures. The parity suite
 * proves TS == C#; these prove specific known-correct behaviours, so a bug
 * mirrored in both the C# engine and its fixtures would still be caught here.
 */
describe('CutThroatRules basics', () => {
  const players = ['p0', 'p1'];

  function freshDeal() {
    const config = cutThroatDoubleSix(2);
    const state = deal(
      config,
      players,
      Partnership.cutThroat(players),
      new SeededRandomSource(42n),
    );
    return { state, rules: new CutThroatRules(config.maxPip) };
  }

  it('opening turn allows exactly one move: the leading double', () => {
    const { state, rules } = freshDeal();
    const legal = rules.getLegalMoves(state);

    expect(legal).toHaveLength(1);
    const move = legal[0]!;
    expect(move.kind).toBe('place');
    if (move.kind === 'place') {
      // A full 28-tile 2P deal always distributes all eight doubles, so the
      // lead is always a double.
      expect(move.tile.isDouble).toBe(true);
      expect(move.player).toBe(state.currentPlayer);
    }
  });

  it('rejects a place by a player who is not on turn', () => {
    const { state, rules } = freshDeal();
    const notCurrent = state.players.find((p) => p !== state.currentPlayer)!;
    const illegal = placeMove(notCurrent, new Tile(6, 6), 'left');

    expect(rules.isLegal(state, illegal)).toBe(false);
  });

  it('allows any participant to resign at any time', () => {
    const { state, rules } = freshDeal();
    for (const p of state.players) {
      expect(rules.isLegal(state, resignMove(p))).toBe(true);
    }
    expect(rules.isLegal(state, resignMove('ghost'))).toBe(false);
  });

  it('opening move that is not the lead is illegal', () => {
    const { state, rules } = freshDeal();
    // Passing on the opening turn is never legal (there is always the lead).
    expect(rules.isLegal(state, passMove(state.currentPlayer))).toBe(false);
  });
});

describe('replayRound validation', () => {
  it('throws when the log does not finish the round', () => {
    expect(() =>
      replayRound({ seed: '42', mode: 'cutthroat', players: ['p0', 'p1'], moves: [] }),
    ).toThrow();
  });

  it('throws on an out-of-range seat', () => {
    expect(() =>
      replayRound({
        seed: '42',
        mode: 'cutthroat',
        players: ['p0', 'p1'],
        moves: [{ playerIndex: 5, kind: 'pass' }],
      }),
    ).toThrow();
  });

  it('throws on an illegal (fabricated) move', () => {
    // p0 cannot legally open with an arbitrary non-lead tile.
    expect(() =>
      replayRound({
        seed: '42',
        mode: 'cutthroat',
        players: ['p0', 'p1'],
        moves: [{ playerIndex: 0, kind: 'place', low: 0, high: 1, end: 'left' }],
      }),
    ).toThrow();
  });
});
