# ADR 0012 — Partner Online (random 2-v-2 matchmaking)

- **Status:** Accepted
- **Date:** 2026-08-09
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0009 (Partner mode), ADR 0010 (random matchmaking), ADR 0011 (auto-start/bots)
- **Supersedes:** the "Cut-Throat only" scope of ADR 0010.

## Context

ADR 0010 shipped random matchmaking for Cut-Throat only, reasoning that random
strangers can't coordinate a Partner team. In practice, random-partner team play
is standard and expected in online games — you're matched *with* a random
partner, not left to organise one. Partner was therefore only reachable via
private Create Room, so it wasn't a real, discoverable online mode.

## Decision

**Add "Partner Online": random matchmaking for Jamaican Partner — a 4-seat,
2-v-2 table where the four matched players are partnered by seat.**

- Teams are the existing seat pairing (`Partnership.AlternatingPairs`: seats
  0 & 2 vs 1 & 3, ADR 0009) — no coordination needed; you're simply dealt a
  partner and opponents.
- Matchmaking reuses ADR 0010's mechanism. `Matchmaking.Properties(mode, size)`
  now publishes `mode: "partner"` (size fixed to 4), so Partner seekers group
  only with each other and never cross-match a Cut-Throat table.
- The lobby surfaces **Cut Throat Online** and **Partner Online (2v2)** side by
  side. Partner Online starts immediately (no size pick); Cut-Throat keeps its
  2/3/4 picker.
- Everything downstream is unchanged: server records `mode: "partner"` at
  `startMatch` (already validated to require 4), the deal uses the Partner
  engine + partnership, auto-start/bot-fill (ADR 0011) fills empty seats with
  bots, and leave handling bot-fills or ends as for Cut-Throat. Casual only,
  as with all random play, until the server roster lands (ADR 0007).

## Consequences

**Positive**
- Partner is now a first-class, discoverable online mode with instant matchmaking,
  reusing the whole existing stack — only a mode parameter and a lobby button.
- Mode/size properties keep Partner and Cut-Throat pools cleanly separated.

**Negative / accepted trade-offs**
- You get a **random** partner — no "play with a specific friend as partner" from
  matchmaking (use a private room for that). A future "invite a partner" flow
  could pre-seat a friend before matchmaking fills the opponents.
- Partner-random inherits the same casual-only settlement and device-authority
  caveats as all random play (ADR 0011).

## References

- `unity/Assets/_Project/Scripts/Core/Utils/Matchmaking.cs` — `Properties(mode, size)`.
- `unity/Assets/_Project/Scripts/Net/PhotonBootstrap.cs` — `QuickMatch(mode, size)`.
- `unity/Assets/_Project/Scripts/Game/LobbyView.cs` — the "Partner Online" entry.
- ADR [`0010-random-matchmaking.md`](0010-random-matchmaking.md) — the mechanism this extends.
