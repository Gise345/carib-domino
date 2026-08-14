import { DealConfig } from './dealConfig';
import { Chain } from './chain';
import { Hand } from './hand';
import { PlayerId } from './ids';
import { MatchState } from './matchState';
import { Partnership } from './partnership';
import { SeededRandomSource } from './prng';
import { findLead } from './startingPlayerRule';
import { Tile } from './tile';

/**
 * Produces the initial `MatchState` by shuffling the tile set with the seeded
 * PRNG and dealing fixed-size hands. Port of `Pose.Core.Dealer`. Deterministic
 * for the same seed + players + config — the property the settlement validator
 * relies on.
 */
/**
 * @param openerIndex - seat that must open the round. -1 (default) uses the
 *   standard rule (highest-double holder leads); a seat >= 0 forces that seat
 *   to lead, which the pose rule uses to seat the previous round's winner.
 * @param freeOpening - when true the opener may lead with any tile (a "free
 *   pose"), not the forced highest double. Ignored when openerIndex is -1.
 */
export function deal(
  config: DealConfig,
  players: readonly PlayerId[],
  partnership: Partnership,
  random: SeededRandomSource,
  openerIndex = -1,
  freeOpening = false,
): MatchState {
  if (players.length < 2) {
    throw new Error('A round requires at least two players.');
  }
  const tilesNeeded = players.length * config.tilesPerHand;
  if (tilesNeeded > config.tileSet.length) {
    throw new Error(
      `Cannot deal ${String(config.tilesPerHand)} tiles to ${String(players.length)} players ` +
        `from a tile set of ${String(config.tileSet.length)} tiles.`,
    );
  }

  const shuffled = shuffleFisherYates(config.tileSet, random);

  const hands: Hand[] = [];
  for (let i = 0; i < players.length; i++) {
    const start = i * config.tilesPerHand;
    hands.push(new Hand(shuffled.slice(start, start + config.tilesPerHand)));
  }

  // Pose rule: a valid openerIndex forces that seat to lead (the previous
  // round's winner). Otherwise the standard rule applies — the highest-double
  // holder leads with that tile.
  let startingIndex: number;
  let free: boolean;
  if (openerIndex >= 0 && openerIndex < players.length) {
    startingIndex = openerIndex;
    free = freeOpening;
  } else {
    const lead = findLead(players, hands, config.maxPip);
    startingIndex = players.indexOf(lead.player);
    if (startingIndex < 0) {
      throw new Error(`Starting player ${lead.player} not found in players list.`);
    }
    free = false;
  }

  return new MatchState({
    players,
    partnership,
    currentPlayerIndex: startingIndex,
    hands,
    chain: Chain.empty,
    turnNumber: 0,
    consecutivePassCount: 0,
    history: [],
    isOver: false,
    freeOpening: free,
  });
}

/**
 * Fisher-Yates shuffle walking from the END down to index 1 — the exact
 * direction and PRNG-draw order the C# dealer uses. Any deviation diverges the
 * deal.
 */
function shuffleFisherYates(source: readonly Tile[], random: SeededRandomSource): Tile[] {
  const shuffled = [...source];
  for (let i = shuffled.length - 1; i > 0; i--) {
    const j = random.nextInt(i + 1);
    const a = shuffled[i];
    const b = shuffled[j];
    if (a === undefined || b === undefined) {
      throw new Error('Shuffle index out of range.');
    }
    shuffled[i] = b;
    shuffled[j] = a;
  }
  return shuffled;
}
