/**
 * Canonical TypeScript rule engine for Pose: Caribbean Dominoes — the server's
 * authoritative copy, kept in lockstep with the C# client engine under
 * `unity/Assets/_Project/Scripts/Core/`. Parity is enforced by replay fixtures
 * (see `functions/test/rules/`). ADR 0007.
 */

export { SeededRandomSource } from './prng';
export type { PlayerId, TeamId } from './ids';
export { Tile } from './tile';
export { doubleSix, doubleSixWithoutDoubleZero } from './tileSet';
export type { ChainEnd } from './chainEnd';
export type { PlacedTile } from './placedTile';
export { Chain } from './chain';
export { Hand } from './hand';
export type { Move, PlaceMove, PassMove, ResignMove } from './move';
export { placeMove, passMove, resignMove, describeMove } from './move';
export { Partnership } from './partnership';
export type { Team } from './partnership';
export type { DealConfig } from './dealConfig';
export { cutThroatDoubleSix } from './dealConfig';
export { MatchState } from './matchState';
export type { MatchStateFields } from './matchState';
export type { MatchEndReason, MatchOutcome } from './matchOutcome';
export { isDraw } from './matchOutcome';
export { findLead } from './startingPlayerRule';
export type { Lead } from './startingPlayerRule';
export { deal } from './dealer';
export { CutThroatRules } from './cutThroatRules';
export { replayRound } from './replay';
export type { ReplayInput, ReplayMove } from './replay';
