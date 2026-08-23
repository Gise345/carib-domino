# ADR 0024 — Quick Partner, and painted game rooms

- **Status:** Accepted
- **Date:** 2026-08-22
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0012 (Partner Online), ADR 0013 (match series), ADR 0016 (economy)
- **Amends:** ADR 0012's matchmaking property set.

## Context

Two things landed together, and one forced the other.

**Partner had no format choice.** Cut-Throat has offered Classic 6 Love (first to
6,000) and Quick Love (first to 3,000) since ADR 0013, but the Partner room
hard-coded `MatchFormat.ClassicSixLove`. A 2-v-2 that always runs the long series
is a worse fit for a lunch break than for a lime, and there was no reason for the
asymmetry beyond the order features were built in.

**The three game rooms did not look like the rest of the app.** Profile and
Settings were rebuilt on the shared `UiKit`; the rooms still wore the old
yellow-and-green banner chrome with loose stacks of buttons, and stated the
stake as prose ("winner takes the pot + 2,000 key bonus") — a copy of the rules
that can drift from them, and one that cannot be right for every table size,
since the pot moves with the number of seats.

## Decision

**1. Quick Partner is the existing Quick Love format, not a new one.**

Partner now offers Classic Partner (`ClassicSixLove`, 6,000) and Quick Partner
(`QuickLove`, 3,000). No new `MatchFormat` member, no rule-engine change on
either side, no new replay fixtures: the format already exists, is already
fixture-tested, and the server's `seriesTarget` is keyed on format alone, with
no knowledge of mode. Quick Partner is genuinely quicker, which the name should
mean.

The alternative considered was "first team to six wins, keys not doubling",
which would have needed a new enum member on the wire and matching changes in
both engines — and would have made "Quick" the *longer* format, since Classic
can end in three games on three keys.

**2. Partner matchmaking splits by format.**

ADR 0012 published `mode` and `size` for Partner and deliberately omitted `fmt`,
because there was only ever one Partner format. Now there are two, and without
this a Classic Partner seeker and a Quick Partner seeker group into the same
Photon session, where only the host's series length applies — the other player
silently gets a match they did not choose. `Matchmaking.Properties` therefore
publishes `fmt` for Partner as well as Cut-Throat.

**3. The rooms are painted, and their numbers are derived.**

- The room's title art *is* the header: no text header, the back ring floats
  over the art, and a brass rule closes the hero off from the cards. Every
  piece of art falls back to a lettered stand-in, so a room ships and reads
  correctly before its art exists.
- Format is chosen by picture; table size is counted in heads rather than read
  as a digit.
- The stake sits on the carved rewards board, which is a *frame* rather than a
  picture — its plank is empty by design, so the numbers are set inside it.
- Those numbers come from `RoomSummary`, which reads the series length from
  `MatchFormatRules`, the clock from `TurnTimer` and the money from `Stakes`.
  Nothing about the stake is typed as a string any more.

`Stakes` is a **display mirror** of `functions/src/lib/economy.ts`, not a second
source of truth: the wallet stays server-authoritative (trust boundary 1), and
`StakesTests` asserts the constants so a server-side change fails a test here
rather than quietly quoting players the wrong pot.

**4. The friends room keeps 2-v-2 as a table shape.**

One-Love With Friends is built exactly like Cut Throat — format, then players,
then rewards. Its old separate Cut-Throat/Partner mode switch is gone; instead
the players row carries a fourth option, "2 v 2 partners", which is a choice
about the shape of the table rather than a second mode selector. Without it,
arranging a private Partner table with friends would no longer be possible.

## Consequences

**Positive**

- Partner gets the format choice Cut-Throat has always had, for the cost of a
  parameter — the hard-coded `ClassicSixLove` is gone.
- A room's stated pot is now correct for the table size by construction; it was
  previously a fixed sentence that was wrong for two of the three table sizes.
- The rooms match the rest of the app, and art can land one file at a time.

**Negative / accepted trade-offs**

- Adding `fmt` to Partner's property set halves each Partner pool. Pre-launch
  this costs nothing; at low concurrency it could mean slower Partner matching,
  and the fix if it bites is bot-fill (ADR 0011), which already exists.
- The format choice is one preference shared across all three rooms rather than
  per-room. Picking Quick in Cut Throat and then opening Partner shows Quick
  Partner. This is deliberate — a player who wants short series wants them
  everywhere — but it is a choice, not a given.
- `Stakes` duplicates two constants across the language boundary. The test is
  the tripwire; there is no shared source for a pure-C#/TypeScript pair.

## Files

- `unity/Assets/_Project/Scripts/Core/Economy/Stakes.cs` — display mirror of the economy.
- `unity/Assets/_Project/Scripts/Core/Presentation/RoomSummary.cs` — what a room states.
- `unity/Assets/_Project/Scripts/Core/Utils/Matchmaking.cs` — `fmt` for Partner.
- `unity/Assets/_Project/Scripts/Game/RoomKit.cs` — hero, tiles, seats, board.
- `unity/Assets/_Project/Scripts/Game/RoomArt.cs` — the art bundle.
- `unity/Assets/_Project/Scripts/Game/LobbyView.cs` — the three rooms.
- `docs/prototypes/room-screens.html` — the mockup these were built from.
