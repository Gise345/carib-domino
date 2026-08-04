# ADR 0011 — Hostless auto-start, bot-fill, and leave handling

- **Status:** Accepted
- **Date:** 2026-08-03
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0006 (N-player online), ADR 0008 (disconnect-as-resign), ADR 0010 (random matchmaking)

## Context

Random matchmaking (ADR 0010) exposed two problems with the earlier online model,
both rooted in a **player-visible "host" role**:

1. **A player controlled when to start.** The 3+P fill flow waited 120s, then
   showed one player a "Start now / keep waiting" prompt. That player was
   privileged, the wait was too long, and it doesn't fit a game meant for
   thousands of concurrent players — nobody should have to press start.
2. **A 2-player game didn't end when the opponent left.** Leave handling
   (ADR 0008) and settlement are both bound to the host: only the host can
   resign a departed seat or settle. With room codes the creator usually stayed;
   random matchmaking made the *host* often the one leaving, stranding the
   remaining player.

The requirement: **no player-facing host, no one deciding when to start, and no
single player's departure breaking the table** — at scale.

## Decision

**Make the table's authority invisible, automatic, and migratable; auto-start
with bot-fill; and handle mid-round leaves without a host.**

Photon Fusion shared mode always has one technical authority (master client) per
table. We do not remove it — we make it invisible and robust:

- **Auto-start, no prompt.** A networked `AutoStartTimer` (`TickTimer`, 60s) opens
  when the table opens. The table deals when it **fills** *or* when the deadline
  elapses — at which point the authority fills every empty seat with a **bot** and
  deals. No "start" button; the 120s prompt is deleted. The timer is networked, so
  a migrated authority honours the same deadline without resetting it.
- **Bots.** `SeatFillPolicy` (pure, tested) decides seat-filling for both moments.
  Bot seats are marked in a networked `BotSeatMask`; the authority drives their
  turns with the existing pure `RandomBot` via `RPC_SubmitMove`. Bots have no
  Firebase uid, so settlement already skips them.
- **Mid-round leave.** On a departure the authority applies `SeatFillPolicy`:
  **two or more humans remain** → replace the leaver's seat with a bot and play on
  (fixes 3P/4P); **one human remains** → resign a departed seat to end the round
  (the lone human wins). This makes a 2P opponent-leave end with a win.
- **Migration.** Authority checks are dynamic (`HasStateAuthority`), not a cached
  host flag. When Fusion migrates authority to a remaining peer, that peer
  re-installs the move validator and resumes the deadline, bot-driving, and leave
  handling. No player's departure — including the current authority's — stalls the
  table.
- **Last-human safety net.** If a client is the only one left in a live round it
  cannot settle (the authority left and nothing migrated here), it ends **locally
  with a win** — casual only, no server stats.

### Scope: casual, shared mode (the chosen fork)

Settlement (`submitRoundLog`) is bound to the original host's uid, so a migrated
authority or a locally-ended round does **not** write stats. That is accepted:
random play is **casual** until the server-authoritative roster lands (ADR 0007).
We explicitly chose to stay on **shared mode with an invisible, migratable
master** now, rather than build **dedicated-server authority** (no peer authority,
real anti-cheat/stakes) — the latter is a larger, later ADR. Shared mode scales to
thousands of concurrent players because those players occupy thousands of
independent 2–4-seat tables; the per-table master is not a global bottleneck.

## Consequences

**Positive**
- No player-facing host, no manual start, 60s cap on waiting, and bot-fill so a
  table never dead-ends — the intended "thousands of players" experience.
- 3P/4P leaves play on with bots; 2P leaves end with a win (the reported bug).
- The seat policy is pure and unit-tested; only the networked timing/migration is
  device-verified.

**Negative / accepted trade-offs**
- **Rounds don't settle across authority-migration / local-end** (no stats when
  the authority was the leaver). Casual-only; closed by the server roster.
- **Authority is still a player's device** — no anti-cheat. Dedicated-server
  authority is the deferred fork.
- **A solo quick-matcher gets a bot table after 60s.** Intended (avoids dead
  tables at low traffic); those games are stats-neutral.
- **Migration robustness depends on Fusion** actually migrating the spawned
  `NetworkedMatch`'s authority; the last-human local-win is the safety net if it
  doesn't. Verified on-device.

## References

- `unity/Assets/_Project/Scripts/Core/Utils/SeatFillPolicy.cs` — seat-fill / leave decision (pure, tested).
- `unity/Assets/_Project/Scripts/Net/NetworkedMatch.cs` — `BotSeatMask`, `AutoStartTimer`, `AutoStartWithBots`, `MakeSeatBot`, `FixedUpdateNetwork`.
- `unity/Assets/_Project/Scripts/Net/OnlineMatchController.cs` — bot-driving, `HandleDepartures`, migration takeover, local-win.
- ADR [`0008-disconnect-as-resign.md`](0008-disconnect-as-resign.md) — the leave→resign path this generalises.
- ADR [`0010-random-matchmaking.md`](0010-random-matchmaking.md) — the casual random play this serves.
- ADR [`0007-settlement-replay-validation.md`](0007-settlement-replay-validation.md) — the host-uid binding and roster gap.
