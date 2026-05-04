# ADR 0001 — Core tech stack

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Supersedes:** none

## Context

We are building a free-to-play multiplayer mobile dominoes game (iOS + Android) with the Caribbean diaspora as its primary audience and a global launch from day one. Eleven rulesets ship at launch. The product targets Ludo-Club-class polish (animation, sound, social) and competes in the casual-mobile category where ARPU is driven primarily by visual feel rather than mechanical novelty.

The stack must support, simultaneously:

1. **Mobile-grade polish on a small team.** A solo full-stack engineer with prior Firebase / React Native experience is the build team. Tooling needs to be mature, well-documented, and not require building bespoke infrastructure for table-stakes features (animation, audio, IAP, ads).
2. **Real-time multiplayer for 2–4 players globally** with sub-200ms p95 turn latency in same-region matches and graceful degradation across regions.
3. **A defensible trust boundary.** Clients must be assumed adversarial. Anything affecting wallets, ELO, leaderboards, or entitlements must be written exclusively by server-side code that validates inputs against a server-issued RNG seed and a deterministic rule engine. See [ARCHITECTURE.md §4](../ARCHITECTURE.md) for the full trust model.
4. **Cross-platform subscription + one-time IAP** with reliable receipt validation, webhook-driven entitlement state, and minimal bespoke server code.
5. **Cost predictability.** Infra spend must stay well below subscription/IAP revenue at any scale we can realistically reach in year one (target: <$2k/mo at 100k MAU).

## Decision

| Layer | Choice |
|---|---|
| Game client | **Unity 6 LTS + C# (.NET Standard 2.1)** |
| Realtime multiplayer | **Photon Fusion 2** (multi-region) |
| Auth, persistence, leaderboards, push, analytics, crashes | **Firebase** (Auth, Firestore, Cloud Messaging, Analytics, Crashlytics) |
| Server-side validation & writes | **Firebase Cloud Functions, Node 20 + TypeScript** |
| Remote configuration & rule definitions | **Firebase Remote Config + Firestore-backed `rulesets` collection** |
| Subscription + IAP | **RevenueCat** |
| Ads (free tier) | **Google AdMob + LevelPlay (IronSource) mediation** |
| Animation | **DOTween** (free) |
| UI text | **TextMeshPro** |
| Asset delivery | **Unity Addressables** |
| Localization | **Unity Localization Package** + Firestore-backed remote string tables |

## Rationale

### Unity 6 LTS for the client

The choice is mostly about earning cheap access to the polish ecosystem rather than because dominoes is technically demanding. Mature, low-friction availability of DOTween, particle systems, skeletal animation, audio engines, prefab/UI tooling, and a well-understood mobile build pipeline (gradle, Xcode, Addressables, IL2CPP) is the difference between a product that earns $0.50 ARPU and one that earns $5. Unity 6 LTS specifically because LTS gives us 2 years of bugfix support and avoids tracking a moving Editor target through a 5–6 month build.

C# with .NET Standard 2.1 keeps the rule engine pure and testable in NUnit (Unity's built-in test runner), which is essential for the parity requirement against the TypeScript engine on the server side.

### Photon Fusion 2 for realtime

Fusion 2 gives us authoritative-host multiplayer, host migration, lag compensation, client prediction, and per-peer state visibility (so opponents' hands are not just hidden in UI but never sent to other clients) out of the box. Multi-region clusters are managed for us, which removes a class of operational work (region capacity, failover, client→region routing) that we have no business doing on a casual mobile budget.

We considered building over WebSockets / Cloudflare Durable Objects directly, but at this stage of the company that trades reliable, well-trodden infrastructure for novel ops work for no product gain. Photon's Gaming Plus pricing (~$95/mo per 500 CCU) projects to ~$400–500/mo at 10k DAU, which is comfortably affordable.

### Firebase for the persistence + auth + push layer

Three reasons:

1. **Existing operational fluency.** The founding engineer has shipped React Native + Firebase products before. New-stack risk is concentrated where it matters (Unity, C#, Photon, dual-language rule engine), not in infra we already know how to run.
2. **Cloud Functions are the natural home for the trust boundary.** Cloud Functions in TypeScript can host the server-side rule engine and the only writes to wallets, ELO, stats, and entitlements. Firestore security rules then default-deny client writes to those collections — a hard boundary, not a polite one.
3. **Built-in coverage** for adjacent services: Auth (anonymous + linked Apple/Google/email), Cloud Messaging, Analytics, Crashlytics, Remote Config. Each of these would otherwise be a separate vendor or build.

Firestore reads dominate over writes in this product (leaderboards, profiles, match history) and aggressive client-side caching keeps cost projections in the $300–800/mo range at 100k MAU.

### RevenueCat for IAP and subscriptions

The receipt-validation, renewal-state, family-sharing, grace-period, and refund-handling logic across both Apple and Google is non-trivial to maintain correctly. RevenueCat solves all of that for free up to $2.5k MTR (then 1% of tracked revenue), and gives us signed webhooks into Cloud Functions so our entitlement state in Firestore is driven by a single trusted source. Even though we have shipped App Store + Play Store IAP via Firebase before, RevenueCat saves enough engineering and removes enough subtle correctness risk (especially around subscription lifecycle events) to be worth the small revenue share above $2.5k MTR.

### AdMob + LevelPlay mediation for free-tier ads

AdMob is the highest-fill ad network for casual mobile. LevelPlay (IronSource) mediation layered on top gives us better effective fill and eCPM by waterfall-bidding across multiple networks. Standard pattern for the category.

### DOTween, TextMeshPro, Addressables, Unity Localization

Standard Unity-ecosystem choices for animation, UI text, dynamic asset delivery, and localization. All four are mature, well-documented, and are the defaults the broader Unity mobile-game community picks. Choosing any alternative would need a strong reason that we don't have.

## Consequences

**Positive**

- New-stack learning is concentrated on Unity + C# + Photon (the client and realtime layer). Backend, IAP, ads, push, auth, analytics, and crash reporting are all on stacks the team has shipped before.
- Trust boundary is enforceable with normal Firestore security rules; no custom auth proxy required.
- Server-side rule engine in TypeScript can be developed, tested, and iterated faster than the C# port; the C# port follows with a shared replay-log fixture corpus.
- Subscription state is sourced from a single signed webhook stream (RevenueCat → CF → Firestore), so the rest of the system reads one source of truth.

**Negative / accepted trade-offs**

- **Dual rule engines (C# client + TS server) must stay in lockstep.** Mitigated by sourcing rule data from Firestore (`/rulesets/{rulesetId}`) and a shared corpus of replay-log fixtures both engines must agree on. Any change requires updating both sides and re-running fixture tests; this is encoded as an explicit anti-pattern in [`CLAUDE.md`](../../CLAUDE.md).
- **Vendor lock-in to Photon for realtime.** Migration off Photon would require reworking Fusion-specific state replication and host-migration code. Acceptable; the alternative is months of bespoke ops work on launch day for no product gain.
- **Unity is heavier than React Native.** Build times are longer, Editor is a separate IDE, the asset pipeline is a new model. Acceptable; the polish ecosystem is the entire reason to be on Unity.
- **RevenueCat takes a small revenue share above $2.5k MTR.** Acceptable; the engineering saved is worth it, and the cap is high enough that we will be profitable by the time we cross it.
- **Three separate vendors (Firebase, Photon, RevenueCat) plus AdMob + LevelPlay** to administer. Mitigated by all five being mature SaaS with stable APIs, signed webhooks, and well-understood operational characteristics.

## Alternatives considered

- **Godot 4 + custom WebSocket netcode.** Better runtime cost, much smaller polish ecosystem, vastly more bespoke work for realtime. Rejected on solo-team capacity and time-to-market.
- **React Native + Skia for rendering.** Faster developer iteration, but the polish ceiling is meaningfully lower for animation-heavy casual-game UI. The competitive product (Ludo Club et al.) is on Unity for a reason.
- **PlayFab / Nakama / GameLift** for backend + matchmaking. PlayFab has the right primitives but a heavier learning curve and weaker IAP/subscription handling than RevenueCat. Nakama is excellent but adds operational burden (we would self-host or pay Heroic Cloud) without saving meaningful work over Firebase + Photon. GameLift overshoots a turn-based game's needs.
- **Apple StoreKit 2 + Play Billing v6 directly.** Free, but the receipt-validation, renewal, refund, and family-sharing logic is non-trivial to maintain across both stores correctly. RevenueCat exists precisely to absorb this work, and free up to $2.5k MTR is a generous floor.
- **Self-hosted Postgres + custom auth.** Strictly more work for no advantage over Firestore + Firebase Auth at this product's read/write profile.

## References

- [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) — full system design, trust boundaries, match lifecycle, data model, anti-cheat strategy.
- [`docs/PROJECT_BRIEF.md`](../PROJECT_BRIEF.md) — product scope, audience, monetization, definition of success.
- [`CLAUDE.md`](../../CLAUDE.md) — repo conventions and development principles enforcing the trust model and dual-engine parity.
