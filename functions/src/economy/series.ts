/**
 * Server-side SERIES accounting (M6) — the authoritative mirror of the client's
 * `Pose.Core.SeriesState` scoring, kept pure so it can be unit-tested and reused
 * by settlement. As each round's log is validated (submitRoundLog), the winning
 * TEAM's points accumulate here; when a team reaches the format target the series
 * is over and the pot pays out. Making the server the series authority closes the
 * pose/opener + winner trust gaps noted in ADR 0007 / 0015. See ADR 0016.
 */

/** Wire form of the match format (mirrors the C# `MatchFormat` wire values). */
export type SeriesFormat = 'classic' | 'quick';

/** Points a round win adds to the winning team (a key adds the bonus instead). */
export const POINTS_PER_WIN = 1_000;

/** Points a key (both-ends lock-out) win adds instead of the flat win. */
export const KEY_POINTS = 2_000;

/** The points target a team must reach to win the series. */
export function seriesTarget(format: SeriesFormat): number {
  return format === 'quick' ? 3_000 : 6_000;
}

/**
 * Folds one validated round into the running team-points tally.
 * @param teamPoints - current points by team id (not mutated)
 * @param winningTeamId - the round's winning team, or null for a draw
 * @param isKey - whether the win was a key (scores the bonus)
 * @returns a new team-points map
 */
export function accumulate(
  teamPoints: Readonly<Record<string, number>>,
  winningTeamId: string | null,
  isKey: boolean,
): Record<string, number> {
  const next: Record<string, number> = { ...teamPoints };
  if (winningTeamId === null) {
    return next; // a draw scores nothing
  }
  const gain = isKey ? KEY_POINTS : POINTS_PER_WIN;
  next[winningTeamId] = (next[winningTeamId] ?? 0) + gain;
  return next;
}

/**
 * The team that has reached the target (highest scorer once any team is at or
 * past it), or null if the series is still running.
 */
export function seriesWinner(
  teamPoints: Readonly<Record<string, number>>,
  format: SeriesFormat,
): string | null {
  const target = seriesTarget(format);
  let best: string | null = null;
  let bestPoints = -1;
  for (const [team, points] of Object.entries(teamPoints)) {
    if (points > bestPoints) {
      bestPoints = points;
      best = team;
    }
  }
  return best !== null && bestPoints >= target ? best : null;
}
