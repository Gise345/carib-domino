import { CutThroatRules } from './cutThroatRules';
import { PlayerId, TeamId } from './ids';
import { isKey } from './keyRule';
import { MatchOutcome } from './matchOutcome';
import { MatchState } from './matchState';
import { Move } from './move';
import { RuleEngine } from './ruleEngine';

/**
 * Jamaican Partner dominoes — 4 players in 2 teams of 2. Port of
 * `Pose.Core.JamaicanPartnerRules`. Move enumeration, legality and turn rotation
 * are IDENTICAL to Cut-Throat (composed, not reimplemented); only end-of-round
 * scoring differs, and is team-based:
 * - Domino: the dominoing player's team wins; score = the opposing team's pips.
 * - Block: the single lowest pip count picks the winning team; a lowest tied
 *   across both teams is a draw.
 * - Resign: the resigner's whole team loses; the opposing team wins; score = the
 *   resigning team's pips.
 */
export class JamaicanPartnerRules implements RuleEngine {
  private readonly moveLogic: CutThroatRules;

  constructor(maxPip = 6) {
    this.moveLogic = new CutThroatRules(maxPip);
  }

  getLegalMoves(state: MatchState): Move[] {
    ensurePartnerSetup(state);
    return this.moveLogic.getLegalMoves(state);
  }

  isLegal(state: MatchState, move: Move): boolean {
    ensurePartnerSetup(state);
    return this.moveLogic.isLegal(state, move);
  }

  apply(state: MatchState, move: Move): MatchState {
    ensurePartnerSetup(state);
    return this.moveLogic.apply(state, move);
  }

  getOutcome(state: MatchState): MatchOutcome | null {
    if (!state.isOver) {
      return null;
    }
    ensurePartnerSetup(state);

    const remaining = new Map<PlayerId, number>();
    for (const [i, p] of state.players.entries()) {
      remaining.set(p, state.handAt(i).pipTotal);
    }

    const last = state.history[state.history.length - 1];
    if (last?.kind === 'resign') {
      return resignOutcome(state, last.player, remaining);
    }

    // Domino: someone emptied their hand — their team wins.
    let dominoerIdx = -1;
    for (let i = 0; i < state.players.length; i++) {
      if (state.handAt(i).count === 0) {
        dominoerIdx = i;
        break;
      }
    }
    if (dominoerIdx >= 0) {
      const dominoer = state.playerAt(dominoerIdx);
      const winningTeam = state.partnership.getTeamOf(dominoer);
      return {
        reason: 'domino',
        winnerId: dominoer,
        winningTeamId: winningTeam,
        winnerScore: sumPipsOfOpposingTeam(state, winningTeam),
        isKey: isKey(state, dominoer),
        remainingPips: remaining,
      };
    }

    return blockOutcome(state, remaining);
  }
}

function resignOutcome(
  state: MatchState,
  resigner: PlayerId,
  remaining: Map<PlayerId, number>,
): MatchOutcome {
  const resigningTeam = state.partnership.getTeamOf(resigner);
  const teams = state.partnership.teams;
  const winningTeam = teams[0]?.id === resigningTeam ? teams[1]?.id : teams[0]?.id;
  if (winningTeam === undefined) {
    throw new Error('Partner partnership is missing a team.');
  }

  let score = 0;
  for (const [i, p] of state.players.entries()) {
    if (state.partnership.getTeamOf(p) === resigningTeam) {
      score += state.handAt(i).pipTotal;
    }
  }

  const winnerId = lowestOnTeam(state, winningTeam);
  return {
    reason: 'resigned',
    winnerId,
    winningTeamId: winningTeam,
    winnerScore: score,
    isKey: false,
    remainingPips: remaining,
  };
}

function blockOutcome(state: MatchState, remaining: Map<PlayerId, number>): MatchOutcome {
  let lowest = Number.MAX_SAFE_INTEGER;
  for (let i = 0; i < state.players.length; i++) {
    lowest = Math.min(lowest, state.handAt(i).pipTotal);
  }

  let winningTeamId: TeamId | null = null;
  let tiedAcrossTeams = false;
  for (const [i, p] of state.players.entries()) {
    if (state.handAt(i).pipTotal !== lowest) {
      continue;
    }
    const team = state.partnership.getTeamOf(p);
    if (winningTeamId === null) {
      winningTeamId = team;
    } else if (winningTeamId !== team) {
      tiedAcrossTeams = true;
      break;
    }
  }

  if (tiedAcrossTeams || winningTeamId === null) {
    return {
      reason: 'blocked',
      winnerId: null,
      winningTeamId: null,
      winnerScore: 0,
      isKey: false,
      remainingPips: remaining,
    };
  }

  // Representative winner: the lowest-indexed player on the winning team at the
  // min pip count (deterministic for replay).
  let winnerId: PlayerId | null = null;
  for (const [i, p] of state.players.entries()) {
    if (state.handAt(i).pipTotal === lowest && state.partnership.getTeamOf(p) === winningTeamId) {
      winnerId = p;
      break;
    }
  }

  return {
    reason: 'blocked',
    winnerId,
    winningTeamId,
    winnerScore: sumPipsOfOpposingTeam(state, winningTeamId),
    isKey: false,
    remainingPips: remaining,
  };
}

/** Lowest-pip player on the given team (the representative winner id). */
function lowestOnTeam(state: MatchState, team: TeamId): PlayerId | null {
  let winner: PlayerId | null = null;
  let lowest = Number.MAX_SAFE_INTEGER;
  for (const [i, p] of state.players.entries()) {
    if (state.partnership.getTeamOf(p) !== team) {
      continue;
    }
    const pips = state.handAt(i).pipTotal;
    if (pips < lowest) {
      lowest = pips;
      winner = p;
    }
  }
  return winner;
}

function sumPipsOfOpposingTeam(state: MatchState, winningTeam: TeamId): number {
  let sum = 0;
  for (const [i, p] of state.players.entries()) {
    if (state.partnership.getTeamOf(p) !== winningTeam) {
      sum += state.handAt(i).pipTotal;
    }
  }
  return sum;
}

function ensurePartnerSetup(state: MatchState): void {
  if (state.players.length !== 4) {
    throw new Error(
      `Jamaican Partner requires exactly 4 players; got ${String(state.players.length)}.`,
    );
  }
  if (state.partnership.teams.length !== 2) {
    throw new Error(
      `Jamaican Partner requires exactly 2 teams; got ${String(state.partnership.teams.length)}.`,
    );
  }
  for (const t of state.partnership.teams) {
    if (t.members.length !== 2) {
      throw new Error(
        `Jamaican Partner requires 2 players per team; team ${t.id} has ${String(t.members.length)}.`,
      );
    }
  }
}
