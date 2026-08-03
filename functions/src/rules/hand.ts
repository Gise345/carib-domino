import { Tile } from './tile';

/**
 * A player's hand. Port of `Pose.Core.Hand` — immutable; `without` returns a new
 * hand with the first matching tile removed, preserving dealt order (which
 * `StartingPlayerRule` and the deal determinism depend on).
 */
export class Hand {
  private readonly tilesArr: readonly Tile[];

  constructor(tiles: readonly Tile[]) {
    this.tilesArr = [...tiles];
  }

  static readonly empty = new Hand([]);

  get count(): number {
    return this.tilesArr.length;
  }

  get tiles(): readonly Tile[] {
    return this.tilesArr;
  }

  /** Sum of pips across every tile. */
  get pipTotal(): number {
    let total = 0;
    for (const t of this.tilesArr) {
      total += t.pips;
    }
    return total;
  }

  contains(tile: Tile): boolean {
    return this.tilesArr.some((t) => t.equals(tile));
  }

  /**
   * Returns a new hand with the first occurrence of `tile` removed.
   * @throws if the tile is not present.
   */
  without(tile: Tile): Hand {
    const idx = this.tilesArr.findIndex((t) => t.equals(tile));
    if (idx < 0) {
      throw new Error(`Hand does not contain ${tile.toString()}.`);
    }
    const next = [...this.tilesArr.slice(0, idx), ...this.tilesArr.slice(idx + 1)];
    return new Hand(next);
  }
}
