import { MatchOutcome } from './matchOutcome';
import { MatchState } from './matchState';
import { Move } from './move';

/**
 * The engine surface both rulesets implement, so replay can pick one by game
 * mode. Cut-Throat and Jamaican Partner share move logic and differ only in
 * end-of-round scoring.
 */
export interface RuleEngine {
  getLegalMoves(state: MatchState): Move[];
  isLegal(state: MatchState, move: Move): boolean;
  apply(state: MatchState, move: Move): MatchState;
  getOutcome(state: MatchState): MatchOutcome | null;
}
