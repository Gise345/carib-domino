# ADR 0010 — Random matchmaking ("Cut Throat Online")

- **Status:** Accepted
- **Date:** 2026-08-03
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0006 (N-player online), ADR 0007 (settlement), ADR 0009 (Partner mode)

## Context

Online play so far required a **room code** — Create Room prints a 6-char code,
the other players type it in. That only connects people who already know each
other. A free-to-play game needs to connect **strangers**: press one thing, get
dropped into a table with whoever else is looking.

Photon Fusion 2 supports this natively. In shared mode, `StartGame` with
`SessionName = null` and a set of `SessionProperties` performs random
matchmaking: the runner joins an open session whose properties match, or (with
`EnableClientSessionCreation`, on by default) creates one. The default
`MatchmakingMode.FillRoom` fills partially-full tables before opening new ones —
exactly the "coalesce seekers into one table" behaviour we want. The session
creator becomes the shared-mode master client, which is already how
`OnlineMatchController` decides who is host — so the entire downstream path
(server seed → deal → replay-validated settlement) is reused unchanged.

## Decision

**Add "Cut Throat Online": one-button random matchmaking, Cut-Throat only,
2–4 players, casual (no stakes) for now.**

- **Cut-Throat only.** Random strangers can't coordinate a Partner team, so
  Jamaican Partner stays room-code-only (gather 4 friends). The lobby button is
  labelled "Cut Throat Online" and offers a 2/3/4 **size** pick, no mode pick.
  A future "Partner Online" entry can be added if desired.
- **Matched by published properties.** `Pose.Core.Matchmaking.CutThroatProperties(size)`
  is the single source of the matchmaking keys — `{ mode: "cutthroat", size: "N" }`.
  It is pure and unit-tested, because a creator and a joiner that build even
  slightly different property sets silently never match. `PhotonBootstrap`
  converts them to `SessionProperty` and does nothing else. The `mode` value
  mirrors the server's wire string (`MatchService` / `startMatch`).
- **Fill behaviour.** A 2P table deals as soon as two are present. 3P/4P reuse
  the existing fill-timeout short-start (ADR 0006): after the wait, the host may
  start with the players present. **Bot-fill** (fill empty seats with bots
  instead of short-starting) is the planned M3.9b follow-up — see below.
- **Casual only for now.** Random matches are stats-light until the
  server-authoritative roster closes the seat→uid trust gap ADR 0007 flags.
  Shipping ELO/wallets on random matches waits for that work.

### Deferred to M3.9b: bot-fill

When not enough humans join in time, the intended behaviour is to **fill the
remaining seats with bots** so a player never dead-ends waiting. That needs a
pure `BotStrategy` in `Pose.Core` and host-side turn-driving in
`OnlineMatchController` (the same `RPC_SubmitMove` mechanism disconnect-resign
already uses), plus a `submitRoundLog` guard so bot seats (no uid) settle
without attribution. Until then, 3P/4P fall back to the short-start prompt.

## Consequences

**Positive**
- Strangers can play with one tap, reusing the whole existing online stack — the
  only new surface is a lobby entry point and Photon session-property matchmaking.
- The matchmaking keys are pinned by a unit test, so a client-side key/value
  drift breaks the build rather than silently splitting players into separate
  empty tables.

**Negative / accepted trade-offs**
- **No bot-fill yet** — a low-traffic 3P/4P seeker can still end up short-starting
  or waiting. Addressed by M3.9b.
- **Casual only** — no ranked random play until the server roster lands.
- **Partner is not matchmakable** — intended; random Partner teams are a poor
  experience. Revisit only if there's demand.
- Matchmaking itself is Photon-runtime and can't be unit-tested; it is verified
  on-device. Only the property builder is covered by tests.

## References

- `unity/Assets/_Project/Scripts/Core/Utils/Matchmaking.cs` — the property keys (pure, tested).
- `unity/Assets/_Project/Scripts/Net/PhotonBootstrap.cs` — `QuickMatch` / random `StartGame`.
- `unity/Assets/_Project/Scripts/Game/LobbyView.cs` — the "Cut Throat Online" entry.
- ADR [`0006-n-player-online.md`](0006-n-player-online.md) — the short-start fill path reused here.
- ADR [`0007-settlement-replay-validation.md`](0007-settlement-replay-validation.md) — the roster gap gating stakes.
