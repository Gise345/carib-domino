/**
 * Which ruleset a round is played under. `cutthroat` is every-player-for-
 * themselves (2–4, solo teams); `partner` is Jamaican Partner (4 players, 2
 * teams of 2). Selects both the rule engine and the partnership at replay time.
 */
export type GameMode = 'cutthroat' | 'partner';
