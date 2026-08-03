import { Tile } from './tile';
import { doubleSix, doubleSixWithoutDoubleZero } from './tileSet';

/**
 * Deal configuration for a round. Port of `Pose.Core.DealConfig`.
 */
export interface DealConfig {
  readonly tileSet: readonly Tile[];
  readonly tilesPerHand: number;
  readonly maxPip: number;
}

/**
 * Standard double-six Cut-Throat config for the given player count. 2 players →
 * 14 tiles each from the full 28; 3 → 9 each from the 27-tile deck (`[0|0]`
 * removed); 4 → 7 each from the full set. Mirrors `DealConfig.CutThroatDoubleSix`.
 * @throws if `playerCount` is not 2, 3 or 4.
 */
export function cutThroatDoubleSix(playerCount: number): DealConfig {
  switch (playerCount) {
    case 2:
      return { tileSet: doubleSix(), tilesPerHand: 14, maxPip: 6 };
    case 3:
      return { tileSet: doubleSixWithoutDoubleZero(), tilesPerHand: 9, maxPip: 6 };
    case 4:
      return { tileSet: doubleSix(), tilesPerHand: 7, maxPip: 6 };
    default:
      throw new RangeError(`Cut-Throat requires 2, 3, or 4 players (got ${String(playerCount)}).`);
  }
}
