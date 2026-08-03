import { Tile } from './tile';

/**
 * A tile as laid in the chain, with explicit pip orientation. Port of
 * `Pose.Core.PlacedTile`: `leftPip` faces the left end of the chain, `rightPip`
 * the right. For the opening tile, `leftPip === tile.a` and `rightPip === tile.b`.
 */
export interface PlacedTile {
  readonly tile: Tile;
  readonly leftPip: number;
  readonly rightPip: number;
}
