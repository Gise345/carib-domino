import { ChainEnd } from './chainEnd';
import { PlacedTile } from './placedTile';
import { Tile } from './tile';

/**
 * The chain of played tiles. Port of `Pose.Core.Chain` — immutable; `place`
 * returns a new chain. Tiles are stored left-to-right and the open pip at each
 * end is derived from the boundary tiles, so legality checks never walk the
 * chain.
 */
export class Chain {
  private readonly tilesList: readonly PlacedTile[];

  private constructor(tiles: readonly PlacedTile[]) {
    this.tilesList = tiles;
  }

  /** The empty chain — the starting point for a new round. */
  static readonly empty = new Chain([]);

  get count(): number {
    return this.tilesList.length;
  }

  get isEmpty(): boolean {
    return this.tilesList.length === 0;
  }

  get tiles(): readonly PlacedTile[] {
    return this.tilesList;
  }

  /** The open pip at the left end. @throws if the chain is empty. */
  get leftEnd(): number {
    const first = this.tilesList[0];
    if (first === undefined) {
      throw new Error('Empty chain has no left end.');
    }
    return first.leftPip;
  }

  /** The open pip at the right end. @throws if the chain is empty. */
  get rightEnd(): number {
    const last = this.tilesList[this.tilesList.length - 1];
    if (last === undefined) {
      throw new Error('Empty chain has no right end.');
    }
    return last.rightPip;
  }

  /**
   * Places `tile` at the requested end and returns a new chain. On an empty
   * chain both ends are open and the tile is laid as-is (a→left, b→right).
   * @throws if the tile does not match the requested end.
   */
  place(tile: Tile, end: ChainEnd): Chain {
    if (this.isEmpty) {
      const first: PlacedTile = { tile, leftPip: tile.a, rightPip: tile.b };
      return new Chain([first]);
    }

    if (end === 'left') {
      const left = this.leftEnd;
      if (!tile.matches(left)) {
        throw new Error(`Tile ${tile.toString()} does not match LEFT end (pip ${String(left)}).`);
      }
      const newOuter = tile.getOther(left);
      const placed: PlacedTile = { tile, leftPip: newOuter, rightPip: left };
      return new Chain([placed, ...this.tilesList]);
    }

    const right = this.rightEnd;
    if (!tile.matches(right)) {
      throw new Error(`Tile ${tile.toString()} does not match RIGHT end (pip ${String(right)}).`);
    }
    const newOuter = tile.getOther(right);
    const placed: PlacedTile = { tile, leftPip: right, rightPip: newOuter };
    return new Chain([...this.tilesList, placed]);
  }
}
