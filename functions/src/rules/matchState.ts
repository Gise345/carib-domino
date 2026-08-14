import { Chain } from './chain';
import { Hand } from './hand';
import { PlayerId } from './ids';
import { Move } from './move';
import { Partnership } from './partnership';

/**
 * Immutable snapshot of an in-progress round. Port of `Pose.Core.MatchState`.
 * Hands are stored as an array parallel to `players` (index i is player i's
 * hand) rather than a dictionary — equivalent, since all access is by player and
 * players are unique, and it sidesteps map-ordering concerns during replay.
 */
export interface MatchStateFields {
  readonly players: readonly PlayerId[];
  readonly partnership: Partnership;
  readonly currentPlayerIndex: number;
  readonly hands: readonly Hand[];
  readonly chain: Chain;
  readonly turnNumber: number;
  readonly consecutivePassCount: number;
  readonly history: readonly Move[];
  readonly isOver: boolean;
  /**
   * True when this round's opening is a "free pose": the opener (the previous
   * round's winner) may lead with any tile, not the forced highest double.
   * Round-level constant; only consulted while the chain is empty. Optional in
   * the fields (defaults false) so pre-pose callers stay valid. Port of
   * `Pose.Core.MatchState.FreeOpening`.
   */
  readonly freeOpening?: boolean;
}

export class MatchState {
  readonly players: readonly PlayerId[];
  readonly partnership: Partnership;
  readonly currentPlayerIndex: number;
  readonly hands: readonly Hand[];
  readonly chain: Chain;
  readonly turnNumber: number;
  readonly consecutivePassCount: number;
  readonly history: readonly Move[];
  readonly isOver: boolean;
  readonly freeOpening: boolean;

  constructor(fields: MatchStateFields) {
    if (fields.players.length < 2) {
      throw new Error('A match requires at least two players.');
    }
    if (fields.currentPlayerIndex < 0 || fields.currentPlayerIndex >= fields.players.length) {
      throw new RangeError('Current player index out of range.');
    }
    if (fields.hands.length !== fields.players.length) {
      throw new Error('Hands array must be parallel to players.');
    }
    this.players = fields.players;
    this.partnership = fields.partnership;
    this.currentPlayerIndex = fields.currentPlayerIndex;
    this.hands = fields.hands;
    this.chain = fields.chain;
    this.turnNumber = fields.turnNumber;
    this.consecutivePassCount = fields.consecutivePassCount;
    this.history = fields.history;
    this.isOver = fields.isOver;
    this.freeOpening = fields.freeOpening ?? false;
  }

  get currentPlayer(): PlayerId {
    const p = this.players[this.currentPlayerIndex];
    if (p === undefined) {
      throw new RangeError('Current player index out of range.');
    }
    return p;
  }

  handAt(index: number): Hand {
    const h = this.hands[index];
    if (h === undefined) {
      throw new RangeError(`No hand at index ${String(index)}.`);
    }
    return h;
  }

  handOf(player: PlayerId): Hand {
    const idx = this.players.indexOf(player);
    if (idx < 0) {
      throw new Error(`Player ${player} is not in this match.`);
    }
    return this.handAt(idx);
  }

  /** The player at seat `index`. @throws if out of range. */
  playerAt(index: number): PlayerId {
    const p = this.players[index];
    if (p === undefined) {
      throw new RangeError(`No player at index ${String(index)}.`);
    }
    return p;
  }

  /** Returns a copy with the listed fields replaced; players/partnership are fixed. */
  with(patch: Partial<Omit<MatchStateFields, 'players' | 'partnership'>>): MatchState {
    return new MatchState({
      players: this.players,
      partnership: this.partnership,
      currentPlayerIndex: patch.currentPlayerIndex ?? this.currentPlayerIndex,
      hands: patch.hands ?? this.hands,
      chain: patch.chain ?? this.chain,
      turnNumber: patch.turnNumber ?? this.turnNumber,
      consecutivePassCount: patch.consecutivePassCount ?? this.consecutivePassCount,
      history: patch.history ?? this.history,
      isOver: patch.isOver ?? this.isOver,
      freeOpening: this.freeOpening,
    });
  }
}
