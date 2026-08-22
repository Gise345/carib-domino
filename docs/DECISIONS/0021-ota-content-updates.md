# ADR 0021 — Over-the-air updates: what is possible, and the plan to get there

- **Status:** Proposed — needs a decision on Phases 1–3 and two new dependencies
- **Date:** 2026-08-21
- **Scope:** `unity`, `functions`, `infra`, `ui`
- **Relates:** [0020-release-pipeline](0020-release-pipeline.md), [0007-settlement-replay-validation](0007-settlement-replay-validation.md), [0016-coin-economy-and-roster](0016-coin-economy-and-roster.md)

## Context

The ask: ship bug fixes, UI changes and updates without a store release — the
CodePush / EAS Update workflow, where a JavaScript bundle is swapped at launch and
users get the fix on next open.

**That workflow does not exist for this app, and cannot be built.** Not as a
limitation of our architecture — as a property of the platform and the store rules.
Everything below is designed around that fact, so it needs stating precisely before
any plan makes sense.

### Why there is no CodePush equivalent

1. **IL2CPP compiles C# ahead of time to native code.** Both platforms build through
   IL2CPP (iOS has no alternative; Android is set to it in
   `ReleaseBuildSettings`). There is no IL at runtime to replace, no assembly to
   load, and `System.Reflection.Emit` does not exist on the target. A `.cs` change
   is a native binary change.

2. **Both stores forbid downloading executable code.** Apple's guideline 2.5.2
   prohibits downloading code that introduces or changes features, with a narrow
   carve-out for JavaScript run in the system web view. Google Play's Device and
   Network Abuse policy bars an app from updating itself by any mechanism other than
   Play's own. React Native gets its exemption precisely because JS-in-a-webview is
   the exception; C# is not.

3. **Switching Android to the Mono backend would not rescue it.** Mono can load
   assemblies at runtime, but iOS still cannot, and doing it would still breach the
   policies above. It is not a route.

**Therefore: anything that is C# today requires a store release, permanently.**

### How we got here

Layers 1–3 below are not new architecture. [ADR 0001](0001-tech-stack.md) specified
all three on day one — "Remote configuration & rule definitions: Firebase Remote
Config + Firestore-backed `rulesets` collection", "Asset delivery: Unity
Addressables", "Localization: ... + Firestore-backed remote string tables" — and the
stack was chosen partly to get them cheaply.

None were implemented. Addressables is configured with zero runtime call sites,
Remote Config was never imported, and `L10n.cs` has no fetch path. This ADR is
therefore mostly a plan to finish ADR 0001, not to extend it.

ADR 0001 also considered and rejected React Native + Skia, on the grounds that its
"polish ceiling is meaningfully lower for animation-heavy casual-game UI". That
holds — and Photon Fusion has no React Native SDK, so it would have meant the custom
WebSocket netcode the same ADR rejected. But the comparison priced React Native's
advantage as "faster developer iteration" and never mentioned over-the-air updates,
which is the larger difference and the one being felt now. Had it been priced, it
would probably not have flipped the engine choice — but it would have made Layers 1–3
a day-one requirement rather than an aspiration.

The procedural UI described below is different: it has **no decision record at all**.
It accreted across feature commits, and it sits at odds with ADR 0001's own rationale,
which cites Unity's "prefab/UI tooling" as part of why the engine was worth choosing.
It should have been raised as an architectural decision when the first view file
passed a few hundred lines, and was not.

### Why that hurts more here than it would in a typical Unity project

The view layer is built procedurally in C#, not from prefabs or data:

| File | Lines |
|---|---|
| `BoardBootstrap.cs` | 2,588 |
| `LobbyView.cs` | 2,339 |
| `TileView.cs` | 980 |
| `BoardRoomHud.cs` | 803 |
| `ShuffleAnimation.cs` | 589 |
| ...rest of `Scripts/Game` | ~2,700 |
| **Total view layer** | **~10,000** |

`LobbyView.cs` alone makes 115 `new GameObject` / `AddComponent<>` / `MakeButton`
calls. The project contains exactly **one** prefab (`NetworkedMatch.prefab`, a Fusion
networked object) and makes **zero** runtime `Addressables.` calls.

The practical consequence: **the share of this app that could be updated over the air
today is approximately zero**, and the cost of OTA is not the delivery plumbing — it
is converting code into content so there is something to deliver.

## Decision

Build OTA as **layers**, each converting one class of change from "code" into
"content or config". Adopt them in cost order, and be explicit that the top layer is
expensive and optional.

### Layer 0 — Server logic *(already live, no work needed)*

Cloud Functions are deployed independently of the client. `settlement`,
`matchmaking`, wallet, leaderboard, profile and invite logic are all fixable with
`firebase deploy --only functions`, today, in minutes.

**Caveat that limits this more than it appears.** ADR 0007 requires the C# and TS
rule engines to produce identical results. A scoring bug fixed only on the server
makes settlement correct while the client still *displays* the wrong thing and offers
the wrong legal moves. Server-only fixes are complete for anything the client never
computes (payouts, ELO, matchmaking, rewards) and only partial for anything it
mirrors (rules, scoring, legality).

### Layer 1 — Remote Config *(cheapest, highest value — do first)*

Firebase Remote Config, fetched at boot with baked-in defaults matching today's
constants, so behaviour is unchanged on day one.

Covers:
- **Economy tuning** — entry stake, key bonus, invite reward, starting balance
  (ADR 0016). Today these are compiled in; a wrong number at soft launch would
  otherwise need a release while live players react to it.
- **Feature kill switches** — disable Facebook login, invites, a broken mode, or ads
  without pulling a build.
- **Timers and pacing** — turn timer, auto-play delay, popup durations.
- **A minimum-version gate** (see Layer 1b).

Does **not** cover: anything structural, any new screen, any logic change.

The same values must be readable by Cloud Functions so the client and server never
disagree about the stake. Remote Config has a server-side SDK; settlement reads the
same keys.

### Layer 1b — Forced-update gate *(do with Layer 1 — it is the safety net)*

A `min_supported_version` key checked at boot. Below it, the app blocks with an
"Update required" screen linking to the store.

This is what makes the whole strategy safe: when something *cannot* be fixed over the
air — which will be most things — you need the ability to guarantee nobody is running
the broken build. Without it, a bad release lives on devices indefinitely.

### Layer 2 — Remote string tables

Unity Localization string tables served remotely, as CLAUDE.md already specifies
("remote string tables in Firestore"). Currently aspirational: `L10n.cs` has no fetch
path and all tables are local.

Covers: copy fixes, typos, tone changes, new languages, per-region wording — without
a release. Meaningful for eleven rulesets across multiple markets.

### Layer 3 — Remote Addressables content

Turn on `BuildRemoteCatalog`, point the remote profile at a host, and move art,
audio, tile skins and board themes into remote groups.

Covers: wrong or ugly art, new tile skins, seasonal themes, sound fixes.

Requires: a host (see Open Questions), catalog versioning, the content-update
workflow (`addressables_content_state.bin` must be retained per release), and a cache
invalidation story. Non-trivial but well-trodden.

Does **not** cover UI changes as things stand, because the UI is not made of assets.

### Layer 4 — Data-driven UI *(expensive; the only thing that delivers the actual ask)*

To change UI without a release, screens must be *content*: prefabs and layout data
loaded through Addressables, driven by existing C# behaviour components, rather than
constructed by C# at runtime.

This is a genuine refactor of ~10,000 lines. It must be incremental — one screen at a
time, each screen's procedural builder replaced by a prefab plus a thin controller —
and each step is independently shippable.

Even fully done, the limit stands: a prefab can only reference scripts that already
exist in the build. New *behaviour* still needs a release. It buys layout, styling,
copy, ordering, and showing or hiding existing elements.

### Layer 5 — Embedded scripting (Lua/xLua/MoonSharp) — **rejected**

An interpreted scripting layer would technically allow logic changes over the air.
Rejected because:

- Apple 2.5.2 permits downloaded JavaScript in a web view, not a Lua VM changing game
  features. Approval is a gamble, and the penalty lands after launch.
- It would fork the codebase into two languages with two mental models.
- It breaks the property the architecture is built on — a pure, testable C# core with
  fixture parity against the TypeScript engine (ADR 0007). Rules in Lua cannot be
  parity-tested against `functions/src/rules/`.
- It puts gameplay-affecting logic outside the trust boundary.

## Plan

Each phase is independently shippable and leaves the app working.

### Phase 1 — Remote Config + version gate
**New dependency:** Firebase Remote Config (Unity SDK + Admin SDK in Functions).

- `Net/RemoteConfigService.cs` — fetch-and-activate at boot, typed accessors, defaults
  matching current constants.
- `Core/GameConstants.cs` — extract every magic economy/timing number currently inline
  into one place, then have it read from the service.
- `functions/src/lib/remoteConfig.ts` — server-side read of the same keys.
- `settlement` reads the stake/bonus from config rather than its own constants.
- `Game/VersionGate.cs` + a blocking "update required" screen with a localization key.
- Tests: defaults applied when fetch fails; server and client resolve the same stake;
  gate triggers below `min_supported_version`.

*Ships: economy tuning, kill switches, forced updates — the highest-value slice.*

### Phase 2 — Remote string tables
- Localization remote table provider; `L10n.cs` gains a fetch path with local
  fallback.
- Publishing path for string tables (same host as Phase 3, decided below).
- Tests: falls back to baked tables offline; remote override wins when present.

*Ships: all copy fixes without a release.*

### Phase 3 — Remote Addressables content
- Enable `BuildRemoteCatalog`, configure the remote profile, split groups
  local vs remote.
- Retain `addressables_content_state.bin` per release (it must be committed or
  archived per build, or content updates cannot be produced).
- Extend `codemagic.yaml` with a content-build-and-publish step.
- Move `TileArtSet` and board/audio assets to remote groups.
- Tests: cold start with no network uses local content; catalog update swaps an asset.

*Ships: art and audio fixes without a release.*

### Phase 4 — Data-driven UI *(only if justified — see recommendation)*
- Convert one screen as a pilot. `LoginView` (439 lines) is the right pilot: smallest,
  self-contained, low regression risk.
- Measure the real cost from the pilot before committing to `LobbyView` (2,339) or
  `BoardBootstrap` (2,588).
- Each converted screen moves to a remote Addressables group.

## Recommendation

**Do Phases 1 and 2 now, before soft launch. Defer Phase 3 until art ships regularly.
Do not start Phase 4 yet.**

Phase 1 is a few days and covers the failure that would actually hurt at launch: a
mis-tuned economy or a feature that needs disabling, live, with real players. The
version gate is what stops any un-fixable bug from becoming permanent.

Phase 4 is the only thing that answers the original ask literally, and it is weeks of
refactoring ~10,000 lines that currently work. Before spending that, it is worth being
honest that with Phase 1 in place a store release is roughly a day's turnaround, and
Play's staged rollout can halt a bad build within hours. For a pre-launch game where
UI is still changing shape weekly, shipping builds is not yet the bottleneck. Revisit
when it is.

## Open questions — needed before Phase 1 starts

1. **Dependency approval.** Firebase Remote Config on the client and in Functions.
   Per CLAUDE.md, new dependencies need explicit sign-off.
2. **Host for remote content** (Phases 2–3): Firebase Hosting, Cloud Storage, or Unity
   Cloud Content Delivery. Firebase keeps everything in one project and one bill;
   CCD is purpose-built for Addressables but adds a vendor. Egress is the cost driver
   either way and depends on asset volume, which is not yet known.
3. **Phase 4 appetite** — pilot `LoginView` to price it, or leave it closed for now?

## Consequences

- Expectations must be set correctly: OTA here means **config, copy and content**, not
  code. Anyone reasoning from a React Native background will assume otherwise.
- Every constant worth tuning has to move out of inline code into one place — good
  hygiene regardless, but it touches many files.
- Remote Config becomes a second source of truth for economy values; client and
  server must read the same keys or settlement and UI will disagree.
- The version gate means a bad `min_supported_version` can brick every client. It
  needs the same care as a security rule, and should only ever be raised after the
  replacement build is live on both stores.
- Phase 3 makes builds and content separable, which means they can also drift.
  Catalog versioning has to be right or old clients break on new content.
