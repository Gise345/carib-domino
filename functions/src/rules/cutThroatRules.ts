import { PlayerId } from './ids';
import { MatchEndReason, MatchOutcome } from './matchOutcome';
import { MatchState } from './matchState';
import { Move, PlaceMove, placeMove, passMove } from './move';
import { findLead } from './startingPlayerRule';

/**
 * Jamaican Cut-Throat rules — every player for themselves, no boneyard. Port of
 * `Pose.Core.CutThroatRules`. A round ends on a domino (empty hand), a block
 * (every player passing in succession), or a resign. The winner scores the sum
 * of the other players' remaining pips; a tied block is a scoreless draw.
 */
export class CutThroatRules {
  private readonly maxPip: number;

  constructor(maxPip = 6) {
    this.maxPip = maxPip;
  }

  getLegalMoves(state: MatchState): Move[] {
    if (state.isOver) {
      return [];
    }

    const player = state.currentPlayer;
    const hand = state.handOf(player);

    if (state.chain.isEmpty) {
      // Opening turn — only the leading tile, by its holder. Re-derived from
      // state so the engine stays a pure function with no dealer-private input.
      const lead = findLead(state.players, state.hands, this.maxPip);
      return [placeMove(player, lead.tile, 'left')];
    }

    const left = state.chain.leftEnd;
    const right = state.chain.rightEnd;
    const moves: Move[] = [];
    for (const tile of hand.tiles) {
      if (tile.matches(left)) {
        moves.push(placeMove(player, tile, 'left'));
      }
      if (tile.matches(right)) {
        moves.push(placeMove(player, tile, 'right'));
      }
    }
    if (moves.length === 0) {
      moves.push(passMove(player));
    }
    return moves;
  }

  isLegal(state: MatchState, move: Move): boolean {
    if (state.isOver) {
      return false;
    }

    // Resign is legal for any participant at any time (disconnect / forfeit).
    if (move.kind === 'resign') {
      return state.players.includes(move.player);
    }

    if (move.player !== state.currentPlayer) {
      return false;
    }

    const legal = this.getLegalMoves(state);
    return legal.some((m) => movesEqual(m, move));
  }

  apply(state: MatchState, move: Move): MatchState {
    if (state.isOver) {
      throw new Error('Cannot apply a move to a finished match.');
    }
    if (!this.isLegal(state, move)) {
      throw new Error(`Illegal move: ${move.kind} by ${move.player}`);
    }

    const newHistory: Move[] = [...state.history, move];

    if (move.kind === 'place') {
      const idx = state.players.indexOf(move.player);
      const newHand = state.handAt(idx).without(move.tile);
      const newHands = [...state.hands];
      newHands[idx] = newHand;
      const newChain = state.chain.place(move.tile, move.end);

      const wonByDomino = newHand.count === 0;
      return state.with({
        currentPlayerIndex: wonByDomino ? state.currentPlayerIndex : nextPlayerIndex(state),
        hands: newHands,
        chain: newChain,
        turnNumber: state.turnNumber + 1,
        consecutivePassCount: 0,
        history: newHistory,
        isOver: wonByDomino,
      });
    }

    if (move.kind === 'pass') {
      const newPassCount = state.consecutivePassCount + 1;
      const blocked = newPassCount >= state.players.length;
      return state.with({
        currentPlayerIndex: blocked ? state.currentPlayerIndex : nextPlayerIndex(state),
        turnNumber: state.turnNumber + 1,
        consecutivePassCount: newPassCount,
        history: newHistory,
        isOver: blocked,
      });
    }

    // resign — ends the round immediately; index left unchanged (GetOutcome
    // reads the resigner from history, not CurrentPlayerIndex).
    return state.with({
      turnNumber: state.turnNumber + 1,
      history: newHistory,
      isOver: true,
    });
  }

  getOutcome(state: MatchState): MatchOutcome | null {
    if (!state.isOver) {
      return null;
    }

    const remaining = new Map<PlayerId, number>();
    for (const [i, player] of state.players.entries()) {
      remaining.set(player, state.handAt(i).pipTotal);
    }

    const last = state.history[state.history.length - 1];
    if (last?.kind === 'resign') {
      return this.resignOutcome(state, last.player, remaining);
    }

    // Domino end: someone has zero tiles.
    let dominoWinnerIdx = -1;
    for (let i = 0; i < state.players.length; i++) {
      if (state.handAt(i).count === 0) {
        dominoWinnerIdx = i;
        break;
      }
    }
    if (dominoWinnerIdx >= 0) {
      const winner = state.playerAt(dominoWinnerIdx);
      let score = 0;
      for (let i = 0; i < state.players.length; i++) {
        if (i !== dominoWinnerIdx) {
          score += state.handAt(i).pipTotal;
        }
      }
      return {
        reason: 'domino',
        winnerId: winner,
        winningTeamId: state.partnership.getTeamOf(winner),
        winnerScore: score,
        remainingPips: remaining,
      };
    }

    // Block end: lowest pip total wins; a tie is a scoreless draw.
    return this.blockOutcome(state, remaining);
  }

  private resignOutcome(
    state: MatchState,
    resigner: PlayerId,
    remaining: Map<PlayerId, number>,
  ): MatchOutcome {
    const resignerPips = state.handOf(resigner).pipTotal;
    const reason: MatchEndReason = 'resigned';

    if (state.players.length === 2) {
      const other = state.playerAt(0) === resigner ? state.playerAt(1) : state.playerAt(0);
      return {
        reason,
        winnerId: other,
        winningTeamId: state.partnership.getTeamOf(other),
        winnerScore: resignerPips,
        remainingPips: remaining,
      };
    }

    // 3+ players: lowest pips among the non-resigners wins (same rule as a block).
    let winner: PlayerId | null = null;
    let lowest = Number.MAX_SAFE_INTEGER;
    for (const [i, p] of state.players.entries()) {
      if (p === resigner) {
        continue;
      }
      const pips = state.handAt(i).pipTotal;
      if (pips < lowest) {
        lowest = pips;
        winner = p;
      }
    }
    return {
      reason,
      winnerId: winner,
      winningTeamId: winner !== null ? state.partnership.getTeamOf(winner) : null,
      winnerScore: resignerPips,
      remainingPips: remaining,
    };
  }

  private blockOutcome(state: MatchState, remaining: Map<PlayerId, number>): MatchOutcome {
    let winnerIdx = -1;
    let lowest = Number.MAX_SAFE_INTEGER;
    let tiedAtLowest = false;
    for (let i = 0; i < state.players.length; i++) {
      const pips = state.handAt(i).pipTotal;
      if (pips < lowest) {
        lowest = pips;
        winnerIdx = i;
        tiedAtLowest = false;
      } else if (pips === lowest) {
        tiedAtLowest = true;
      }
    }

    if (tiedAtLowest || winnerIdx < 0) {
      return {
        reason: 'blocked',
        winnerId: null,
        winningTeamId: null,
        winnerScore: 0,
        remainingPips: remaining,
      };
    }

    const winner = state.playerAt(winnerIdx);
    let score = 0;
    for (let i = 0; i < state.players.length; i++) {
      if (i !== winnerIdx) {
        score += state.handAt(i).pipTotal;
      }
    }
    return {
      reason: 'blocked',
      winnerId: winner,
      winningTeamId: state.partnership.getTeamOf(winner),
      winnerScore: score,
      remainingPips: remaining,
    };
  }
}

function nextPlayerIndex(state: MatchState): number {
  return (state.currentPlayerIndex + 1) % state.players.length;
}

function movesEqual(a: Move, b: Move): boolean {
  if (a.player !== b.player) {
    return false;
  }
  if (a.kind === 'place' && b.kind === 'place') {
    return sameTile(a, b) && a.end === b.end;
  }
  return a.kind === 'pass' && b.kind === 'pass';
}

function sameTile(a: PlaceMove, b: PlaceMove): boolean {
  return a.tile.equals(b.tile);
}
