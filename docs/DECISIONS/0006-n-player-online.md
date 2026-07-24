# ADR 0006 — 3- and 4-player online (NetworkedMatch contract v3)

- **Status:** Accepted
- **Date:** 2026-07-19
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Supersedes / extends:** [`0005-in-place-online-rematch.md`](0005-in-place-online-rematch.md) (rematch contract v2)

## Context

Online Cut-Throat shipped 2-player only (M3.3–M3.6). The rule engine, dealer,
partnership model, turn rotation, block detection and outcome logic were already
written to be generic over `Players.Count` and already support 2, 3 and 4
players — the offline path deals 4. All the "exactly two players" assumptions
lived in the networking layer (`NetworkedMatch`, `OnlineMatchController`), the
lobby (no player-count choice) and online seating (`SeatPlayersForOnline` mapped
one opponent to the top seat).

Extending to 3–4 players changes a published Photon networked contract, so it
warrants a decision record.

## Decision

**Generalize `NetworkedMatch` to N players (2–4), contract v3:**

| v2 (2-player) | v3 (N-player) |
|---|---|
| `Player1Id` / `Player2Id` | `NetworkArray<NetworkString<_32>> PlayerIds` (cap 4) + `PlayerCount` |
| implicit host=0 / joiner=1 | `NetworkArray<int> SeatPlayerRefs` — each seat's owning `PlayerRef.PlayerId` |
| `RPC_RegisterPlayer2` (one joiner flips `DealReady`) | `RPC_RegisterPlayer` appends to the next seat; `DealReady` flips when `RegisteredCount == PlayerCount` |
| `Player1WantsRematch` / `Player2WantsRematch` | `RematchVoteMask` (bit per seat); re-deal when all seats' bits set |
| — | `RegisteredCount` (for pre-deal waiting UI) |
| — | `StartWithCurrentPlayers()` (host trims target to current count) |
| `MaxMoves = 64` | `MaxMoves = 128` |

Key choices:

1. **Seat identity by `PlayerRef`, not join order or display name.** The host
   records the sender's `RpcInfo.Source.PlayerId` per seat. Each client finds
   its own seat by matching `Runner.LocalPlayer.PlayerId`. This is robust
   against the adjective-noun name generator producing duplicate display names,
   and against replication-order races.

2. **Player count is a fixed pick by the host** at Create time (a 2/3/4 selector
   on the lobby's Create panel), threaded through `PhotonBootstrap.CreateRoom`
   (which sets the Fusion room capacity) and `OnlineMatchController.Setup`.
   Joiners never choose — they read `PlayerCount` from the replicated match.

3. **Fill-timeout → play short.** For a 3+ player room the host runs a
   2-minute timer while waiting. If it expires with ≥2 but < target players, the
   host is prompted to start with whoever is present; accepting calls
   `StartWithCurrentPlayers()`, which trims `PlayerCount` down to
   `RegisteredCount` and trips the existing "all seats filled → deal" path.
   Declining re-arms the timer. If the room fills first, it deals automatically.

4. **Mid-game leave ends the match for everyone** (for now). Continuing a
   dominoes round after a player leaves is genuinely complex — their hand
   affects block detection and the boneyard — so the existing "someone left →
   back to lobby" behaviour is kept and relabelled. "Continue without them" is
   deferred.

5. **Seating extracted to pure `Pose.Core.SeatArrangement`.** Mapping
   `(playerCount, localIndex)` → the four fixed table seats (local always
   Bottom, others in turn order) is Unity-free and unit-tested for 2/3/4 and
   every local seat, following the same testable-extraction approach as
   `ChainLayout` / `MatchSignalTracker`. `BoardBootstrap.SeatPlayersForOnline`
   is a thin consumer.

## Consequences

**Positive**

- No rule-engine change — the Core was already N-ready, so this is confined to
  transport + UI.
- The rematch handshake (v2) generalizes cleanly: a bitmask over seats instead
  of two bools.
- Seat mapping is provably correct off-device (unit tests) rather than eyeballed.

**Negative / accepted trade-offs**

- **3–4P still can't survive a mid-game disconnect** — any leave ends the round.
  Tracked for a later "play on vs bot / redistribute" slice.
- **Short-start relies on the host** being present and awake at the 2-minute
  mark; an AFK host never starts short. Acceptable for now.
- **A player leaving during the pre-deal window** leaves a filled seat with no
  live owner (registration is append-only); the deal could include a ghost.
  Edge case in the brief waiting window — not handled in this slice.
- **Client-chosen seed remains exploitable until M4** — unchanged from v2;
  `NextSeedProvider` is still the seam for a server-issued seed.

## References

- `unity/Assets/_Project/Scripts/Net/NetworkedMatch.cs` — contract v3.
- `unity/Assets/_Project/Scripts/Net/OnlineMatchController.cs` — N-player deal, PlayerRef seat, bitmask votes, short-start passthrough.
- `unity/Assets/_Project/Scripts/Core/Presentation/SeatArrangement.cs` + `SeatArrangementTests.cs` — seat mapping.
- `unity/Assets/_Project/Scripts/Game/LobbyView.cs` — player-count picker.
- `unity/Assets/_Project/Scripts/Net/PhotonBootstrap.cs` — room capacity.
- ADR [`0005-in-place-online-rematch.md`](0005-in-place-online-rematch.md) — the rematch contract this extends.
