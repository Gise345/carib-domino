# ADR 0021 — Over-the-air updates: what can change without a store build

- **Status:** Accepted (hosting + strategy landed; Remote Config client + Addressables remote groups pending)
- **Date:** 2026-08-19
- **Scope:** `infra`, `net`, `docs`
- **Relates:** [0020-release-pipeline](0020-release-pipeline.md) (store builds), [0016](0016-coin-economy-and-roster.md) (server-authoritative economy)

## Context

Every compiled-code change means a new AAB/IPA and a store upload. That is slow and,
for genuine bug fixes, unavoidable — Apple guideline 2.5.2 forbids downloading
executable code, and the Unity C# hot-patch frameworks (HybridCLR, ILRuntime, xLua)
violate it *and* impose a large architecture. So we do **not** hot-patch C#.

Instead we minimise how often a rebuild is needed by keeping as much as possible
*off* the compiled client. This ADR records which mechanism owns which kind of
change, so the reflex before rebuilding is "can this be a function, a config value,
a remote string, or an addressable asset instead?"

## Decision

| Change | Mechanism | Rebuild? |
|---|---|---|
| Server logic — scoring, settlement, economy, matchmaking, social, friend resolution | **Cloud Functions** (`firebase deploy --only functions`) | No |
| Flags, kill-switch, tunable client numbers, force-update gate, client-facing URLs | **Firebase Remote Config** | No |
| User-facing text / localization | **remote string tables in Firestore** (existing) | No |
| Art, tile skins, board themes, avatars, data-driven content | **Addressables remote catalog** (this ADR) | No |
| C# logic, scene structure, new UI, SDK changes | new build | **Yes** |

### Remote Config (client)

A `RemoteConfigService` fetches + activates at launch over in-app defaults, exposing
typed getters. Keys are **non-authoritative only** — feature flags, a maintenance
kill-switch, a `min_supported_build` force-update gate, and the legal URLs. Anything
that guards a wallet/stat/ELO/entitlement stays server-authoritative in Cloud
Functions; client Remote Config for those would only change display, never the rule.

### Addressables remote catalog (hosting decision)

Addressables is already installed (Localization runs on it). To make content OTA we
host the catalog + bundles and point the `Remote` profile at that URL.

**Host = a dedicated Firebase Hosting site**, not the marketing site and not Cloud
Storage:
- Firebase Hosting is a global CDN with per-path cache headers — right for
  content-hashed bundles (immutable, 1-year cache) vs the catalog `.json`/`.hash`
  (short cache, so a new catalog is picked up).
- A **separate site** (`content` target) keeps large binary bundles out of the
  marketing site's repo/deploy and lets content ship on its own cadence. `firebase.json`
  hosting is now a **targeted array** (`web` + `content`); the marketing config is
  unchanged beyond gaining `"target": "web"`.
- Cloud Storage was rejected: its public download URLs are `%2F`-encoded and it is
  not a CDN — awkward as an Addressables `RemoteLoadPath`.

Bundles build into `content/public/<BuildTarget>/` (git-ignored build output; only
the placeholder `index.html` is tracked) and deploy with
`firebase deploy --only hosting:content`.

## Consequences

- **Positive:** A large class of "fix" — server rules, a bad number, a wrong URL, a
  feature that must be turned off, new art — ships with no store round-trip. Play
  internal testing already propagates a real build in minutes; this removes the build
  entirely for these.
- **Cost:** Two more moving parts to operate (a Remote Config console + a content
  deploy). The Addressables remote catalog only pays off once there is remote content
  to push — until then it is set-up-and-wait. Localization is already OTA via Firestore,
  so its Addressables groups need not be made remote.
- **Boundary intact:** OTA never touches authoritative game rules or wallets — those
  are Cloud Functions, which are themselves deployed independently of the app.

## Follow-ups

- Import `FirebaseRemoteConfig` (SDK module) → write `RemoteConfigService` + keys + test.
- Create the `content` Hosting site and apply targets (see `docs/SETUP/addressables-remote.md`).
- Mark real content groups (tile skins, board themes) `Remote` once that art exists.
