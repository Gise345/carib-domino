import { ChainEnd } from './chainEnd';
import { cutThroatDoubleSix } from './dealConfig';
import { CutThroatRules } from './cutThroatRules';
import { deal } from './dealer';
import { GameMode } from './gameMode';
import { PlayerId } from './ids';
import { JamaicanPartnerRules } from './jamaicanPartnerRules';
import { MatchOutcome } from './matchOutcome';
import { Move, passMove, placeMove, resignMove } from './move';
import { Partnership } from './partnership';
import { SeededRandomSource } from './prng';
import { RuleEngine } from './ruleEngine';
import { Tile } from './tile';

/**
 * One move in a submitted round log. The wire form mirrors the client's
 * `NetworkedMove`: a seat index plus the tile/end for placements. Pip/end fields
 * are ignored for pass/resign.
 */
export interface ReplayMove {
  readonly playerIndex: number;
  readonly kind: 'place' | 'pass' | 'resign';
  // `| undefined` (not just `?`) so a Zod-parsed payload — where absent optional
  // fields surface as explicit `undefined` under exactOptionalPropertyTypes — is
  // assignable here.
  readonly low?: number | undefined;
  readonly high?: number | undefined;
  readonly end?: ChainEnd | undefined;
}

/**
 * Everything the server needs to reconstruct a round: the (server-issued) seed
 * as a decimal string, the game mode (which selects ruleset + partnership), the
 * players in seat order, and the move log.
 */
export interface ReplayInput {
  readonly seed: string;
  readonly mode: GameMode;
  readonly players: readonly PlayerId[];
  readonly moves: readonly ReplayMove[];
  /**
   * Pose rule (ADR 0015). The seat that opened the round: -1 / omitted means the
   * standard forced open (highest double leads); a seat >= 0 forces that seat.
   * `freeOpening` lets that opener lead with any tile. These describe the SERIES
   * context of the round and must be server-derived from the previous round's
   * winner — never trusted from the client — exactly like `seed` and `mode`.
   */
  readonly openerIndex?: number | undefined;
  readonly freeOpening?: boolean | undefined;
}

/**
 * The partnership for a mode: solo teams for Cut-Throat, alternating pairs
 * (0+2 / 1+3) for Jamaican Partner. The mode is server-recorded, so the caller
 * can't change the outcome by lying about it (ADR 0009).
 */
export function partnershipFor(mode: GameMode, players: readonly PlayerId[]): Partnership {
  if (mode === 'partner') {
    if (players.length !== 4) {
      throw new Error('Jamaican Partner requires exactly 4 players.');
    }
    const [a, b, c, d] = players;
    if (a === undefined || b === undefined || c === undefined || d === undefined) {
      throw new Error('Jamaican Partner requires 4 named players.');
    }
    return Partnership.alternatingPairs(a, b, c, d);
  }
  return Partnership.cutThroat(players);
}

function ruleEngineFor(mode: GameMode, maxPip: number): RuleEngine {
  return mode === 'partner' ? new JamaicanPartnerRules(maxPip) : new CutThroatRules(maxPip);
}

/**
 * Deals from the seed and applies every logged move through the canonical rule
 * engine for the given mode, returning the authoritative outcome. Throws if any
 * move is illegal, out of turn, or references a bad seat — i.e. the submitted
 * log could not have happened — or if the log doesn't finish the round.
 *
 * This is the trust anchor for settlement: the server never believes a claimed
 * result, it recomputes one from raw inputs (ADR 0007).
 */
export function replayRound(input: ReplayInput): MatchOutcome {
  const playerCount = input.players.length;
  const config = cutThroatDoubleSix(playerCount);
  const partnership = partnershipFor(input.mode, input.players);
  const rng = new SeededRandomSource(BigInt(input.seed));

  let state = deal(
    config,
    input.players,
    partnership,
    rng,
    input.openerIndex ?? -1,
    input.freeOpening ?? false,
  );
  const rules = ruleEngineFor(input.mode, config.maxPip);

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
