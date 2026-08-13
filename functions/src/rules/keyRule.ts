import { PlayerId } from './ids';
import { MatchState } from './matchState';

/**
 * Detects a "key" — a both-ends lock-out win. A player who empties their hand
 * scores a key when the winning tile was playable on BOTH open ends (a capicúa)
 * AND no opponent still holds either of those two end numbers (so the board was
 * truly locked). Worth the key bonus instead of the flat win. Port of
 * `Pose.Core.KeyRule` — must stay in lockstep with the C# engine.
 *
 * @param state - the finished match state (a domino end has already been established)
 * @param winner - the player who emptied their hand
 * @returns true if the hand-emptying win is a key
 */
export function isKey(state: MatchState, winner: PlayerId): boolean {
  const last = state.history[state.history.length - 1];
  if (last?.kind !== 'place') {
    return false;
  }

  // The end NOT played on is unchanged by the winning move; if the winning tile
  // also matches it, the tile was playable on both ends.
  const otherEnd = last.end === 'left' ? state.chain.rightEnd : state.chain.leftEnd;
  if (!last.tile.matches(otherEnd)) {
    return false;
  }

  // Lock-out: no opponent holds either of the winning tile's pips (i.e. no one
  // could have played on either of the two locked ends).
  const a = last.tile.a;
  const b = last.tile.b;
  for (const [i, p] of state.players.entries()) {
    if (p === winner) {
      continue;
    }
    for (const tile of state.handAt(i).tiles) {
      if (tile.matches(a) || tile.matches(b)) {
        return false;
      }
    }
  }
  return true;
}
