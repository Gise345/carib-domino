import { Hand } from './hand';
import { PlayerId } from './ids';
import { Tile } from './tile';

/** Who leads and with which tile. Port of `StartingPlayerRule.Lead`. */
export interface Lead {
  readonly player: PlayerId;
  readonly tile: Tile;
}

/**
 * Finds the opening lead. Port of `Pose.Core.StartingPlayerRule.FindLead`: the
 * holder of the highest double leads with it; if nobody holds a double, the
 * holder of the highest-pip single tile leads, ties broken by player order.
 *
 * `hands` is parallel to `players` (index i is player i's hand).
 */
export function findLead(
  players: readonly PlayerId[],
  hands: readonly Hand[],
  maxPip: number,
): Lead {
  for (let d = maxPip; d >= 0; d--) {
    const target = new Tile(d, d);
    for (let i = 0; i < players.length; i++) {
      const hand = hands[i];
      const player = players[i];
      if (hand !== undefined && player !== undefined && hand.contains(target)) {
        return { player, tile: target };
      }
    }
  }

  // No doubles anywhere — fall back to the highest single tile.
  const firstPlayer = players[0];
  if (firstPlayer === undefined) {
    throw new Error('findLead requires at least one player.');
  }
  let bestPlayer = firstPlayer;
  let bestTile = new Tile(0, 0);
  let bestPips = -1;
  for (const [i, player] of players.entries()) {
    const hand = hands[i];
    if (hand === undefined) {
      continue;
    }
    for (const t of hand.tiles) {
      if (t.pips > bestPips) {
        bestPips = t.pips;
        bestPlayer = player;
        bestTile = t;
      }
    }
  }
  return { player: bestPlayer, tile: bestTile };
}
