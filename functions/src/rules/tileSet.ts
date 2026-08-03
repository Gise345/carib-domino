import { Tile } from './tile';

/**
 * Standard domino tile-set factories. Port of `Pose.Core.TileSet`. Generation
 * ORDER matters: the Fisher-Yates shuffle in the dealer consumes the set in this
 * exact order, so any reordering here would diverge from the C# deal.
 */

function generate(maxPip: number): Tile[] {
  const tiles: Tile[] = [];
  for (let a = 0; a <= maxPip; a++) {
    for (let b = a; b <= maxPip; b++) {
      tiles.push(new Tile(a, b));
    }
  }
  return tiles;
}

/** Standard double-six set: 28 tiles, `[0|0]`..`[6|6]`. */
export function doubleSix(): Tile[] {
  return generate(6);
}

/**
 * The 3-player Jamaican Cut-Throat deck: double-six minus `[0|0]`, so the 27
 * tiles divide evenly into three 9-tile hands. Order is preserved (the `[0|0]`
 * is simply skipped), matching `DealConfig.DoubleSixWithoutDoubleZero`.
 */
export function doubleSixWithoutDoubleZero(): Tile[] {
  return doubleSix().filter((t) => !(t.a === 0 && t.b === 0));
}
