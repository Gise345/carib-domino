import { ChainEnd } from './chainEnd';
import { cutThroatDoubleSix } from './dealConfig';
import { CutThroatRules } from './cutThroatRules';
import { deal } from './dealer';
import { PlayerId } from './ids';
import { MatchOutcome } from './matchOutcome';
import { Move, passMove, placeMove, resignMove } from './move';
import { Partnership } from './partnership';
import { SeededRandomSource } from './prng';
import { Tile } from './tile';

/**
 * One move in a submitted round log. The wire form mirrors the client's
 * `NetworkedMove`: a seat index plus the tile/end for placements. Pip/end fields
 * are ignored for pass/resign.
 */
export interface ReplayMove {
  readonly playerIndex: number;
  readonly kind: 'place' | 'pass' | 'resign';
  readonly low?: number;
  readonly high?: number;
  readonly end?: ChainEnd;
}

/**
 * Everything the server needs to reconstruct a Cut-Throat round: the (server-
 * issued) seed as a decimal string, the players in seat order, and the move log.
 */
export interface ReplayInput {
  readonly seed: string;
  readonly players: readonly PlayerId[];
  readonly moves: readonly ReplayMove[];
}

/**
 * Deals from the seed and applies every logged move through the canonical rule
 * engine, returning the authoritative outcome. Throws if any move is illegal,
 * out of turn, or references a bad seat — i.e. the submitted log could not have
 * happened — or if the log doesn't actually finish the round.
 *
 * This is the trust anchor for settlement: the server never believes a claimed
 * result, it recomputes one from raw inputs (ADR 0007).
 */
export function replayRound(input: ReplayInput): MatchOutcome {
  const playerCount = input.players.length;
  const config = cutThroatDoubleSix(playerCount);
  const partnership = Partnership.cutThroat(input.players);
  const rng = new SeededRandomSource(BigInt(input.seed));

  let state = deal(config, input.players, partnership, rng);
  const rules = new CutThroatRules(config.maxPip);

  for (const [i, m] of input.moves.entries()) {
    state = rules.apply(state, toMove(m, input.players, i));
  }

  const outcome = rules.getOutcome(state);
  if (outcome === null) {
    throw new Error('Replay ended before the round was over.');
  }
  return outcome;
}

function toMove(m: ReplayMove, players: readonly PlayerId[], moveIndex: number): Move {
  const player = players[m.playerIndex];
  if (player === undefined) {
    throw new Error(
      `Move ${String(moveIndex)} references seat ${String(m.playerIndex)}, out of range.`,
    );
  }
  switch (m.kind) {
    case 'pass':
      return passMove(player);
    case 'resign':
      return resignMove(player);
    case 'place': {
      if (m.low === undefined || m.high === undefined || m.end === undefined) {
        throw new Error(`Move ${String(moveIndex)} is a place but is missing tile/end fields.`);
      }
      return placeMove(player, new Tile(m.low, m.high), m.end);
    }
  }
}
