/**
 * A single domino tile. Port of `Pose.Core.Tile`. Tiles are symmetric —
 * `[3,5]` equals `[5,3]` — so pips are stored canonically with the smaller in
 * `a`.
 */
export class Tile {
  readonly a: number;
  readonly b: number;

  constructor(a: number, b: number) {
    if (a <= b) {
      this.a = a;
      this.b = b;
    } else {
      this.a = b;
      this.b = a;
    }
  }

  /** Total pip value (a + b). */
  get pips(): number {
    return this.a + this.b;
  }

  /** True when both pips are equal (a "double"). */
  get isDouble(): boolean {
    return this.a === this.b;
  }

  /**
   * The pip on the opposite end from `pip`. For doubles, returns the same pip.
   * @throws if the tile does not contain `pip`.
   */
  getOther(pip: number): number {
    if (this.a === pip) {
      return this.b;
    }
    if (this.b === pip) {
      return this.a;
    }
    throw new Error(`Tile ${this.toString()} does not contain pip ${String(pip)}.`);
  }

  /** True if either pip equals `pip`. */
  matches(pip: number): boolean {
    return this.a === pip || this.b === pip;
  }

  equals(other: Tile): boolean {
    return this.a === other.a && this.b === other.b;
  }

  toString(): string {
    return `[${String(this.a)}|${String(this.b)}]`;
  }
}
