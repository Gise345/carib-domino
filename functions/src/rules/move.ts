import { ChainEnd } from './chainEnd';
import { PlayerId } from './ids';
import { Tile } from './tile';

/**
 * A player's action. Port of the C# `Move` hierarchy (`PlaceMove` / `PassMove`
 * / `ResignMove`) as a discriminated union — idiomatic TS and trivial to pattern
 * match in the rule engine.
 */
export type Move = PlaceMove | PassMove | ResignMove;

export interface PlaceMove {
  readonly kind: 'place';
  readonly player: PlayerId;
  readonly tile: Tile;
  readonly end: ChainEnd;
}

export interface PassMove {
  readonly kind: 'pass';
  readonly player: PlayerId;
}

export interface ResignMove {
  readonly kind: 'resign';
  readonly player: PlayerId;
}

export function placeMove(player: PlayerId, tile: Tile, end: ChainEnd): PlaceMove {
  return { kind: 'place', player, tile, end };
}

export function passMove(player: PlayerId): PassMove {
  return { kind: 'pass', player };
}

export function resignMove(player: PlayerId): ResignMove {
  return { kind: 'resign', player };
}

export function describeMove(move: Move): string {
  switch (move.kind) {
    case 'place':
      return `${move.player} PLACE ${move.tile.toString()} on ${move.end}`;
    case 'pass':
      return `${move.player} PASS`;
    case 'resign':
      return `${move.player} RESIGN`;
  }
}
