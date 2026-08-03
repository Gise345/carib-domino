import { PlayerId, TeamId } from './ids';

/**
 * A team sharing one score. Port of `Pose.Core.Team`.
 */
export interface Team {
  readonly id: TeamId;
  readonly members: readonly PlayerId[];
}

/**
 * The team configuration for a round. Port of `Pose.Core.Partnership`. Cut-Throat
 * is modelled as one solo team per player; partner variants pair players. The
 * engine reads but never mutates it.
 */
export class Partnership {
  readonly teams: readonly Team[];
  private readonly teamByPlayer: Map<PlayerId, TeamId>;

  constructor(teams: readonly Team[]) {
    if (teams.length === 0) {
      throw new Error('Partnership must contain at least one team.');
    }

    const seenTeamIds = new Set<TeamId>();
    const teamByPlayer = new Map<PlayerId, TeamId>();
    for (const t of teams) {
      if (seenTeamIds.has(t.id)) {
        throw new Error(`Duplicate TeamId '${t.id}' in partnership.`);
      }
      seenTeamIds.add(t.id);
      for (const p of t.members) {
        if (teamByPlayer.has(p)) {
          throw new Error(`Player ${p} appears in more than one team.`);
        }
        teamByPlayer.set(p, t.id);
      }
    }

    this.teams = teams;
    this.teamByPlayer = teamByPlayer;
  }

  /** The team the player belongs to. @throws if the player is unteamed. */
  getTeamOf(player: PlayerId): TeamId {
    const teamId = this.teamByPlayer.get(player);
    if (teamId === undefined) {
      throw new Error(`Player ${player} is not a member of any team in this partnership.`);
    }
    return teamId;
  }

  /**
   * Cut-Throat: each player gets a solo team named `team:{player}` — matching the
   * C# naming so `winningTeamId` strings are identical across languages.
   */
  static cutThroat(players: readonly PlayerId[]): Partnership {
    if (players.length === 0) {
      throw new Error('Cut-Throat partnership requires at least one player.');
    }
    const seen = new Set<PlayerId>();
    const teams: Team[] = [];
    for (const p of players) {
      if (seen.has(p)) {
        throw new Error(`Duplicate player ${p} in Cut-Throat partnership.`);
      }
      seen.add(p);
      teams.push({ id: `team:${p}`, members: [p] });
    }
    return new Partnership(teams);
  }

  /**
   * Jamaican Partner: positions 0+2 form team_a, positions 1+3 form team_b
   * (partners across the table). All four players must be distinct. Port of
   * `Partnership.AlternatingPairs`.
   */
  static alternatingPairs(p1: PlayerId, p2: PlayerId, p3: PlayerId, p4: PlayerId): Partnership {
    const seen = new Set<PlayerId>([p1]);
    if (!add(seen, p2) || !add(seen, p3) || !add(seen, p4)) {
      throw new Error('AlternatingPairs requires four distinct players.');
    }
    const teamA: Team = { id: 'team_a', members: [p1, p3] };
    const teamB: Team = { id: 'team_b', members: [p2, p4] };
    return new Partnership([teamA, teamB]);
  }
}

function add(set: Set<PlayerId>, value: PlayerId): boolean {
  if (set.has(value)) {
    return false;
  }
  set.add(value);
  return true;
}
