# Pose: Caribbean Dominoes

> Brand mark: **Pose** · Store name: **Pose: Caribbean Dominoes** · Subtitle: *Jamaican, Mexican, Cuban, Slam*
> See [`docs/DECISIONS/0002-product-name.md`](./docs/DECISIONS/0002-product-name.md) for the naming rationale.

A polished, multiplayer mobile dominoes game in the spirit of Ludo Club — same level of animation polish, sound design, social features, and tactile satisfaction — but built around dominoes, with the Caribbean variants (Jamaican Partner, Cuban, Trinidadian, Puerto Rican) as the cultural and marketing centrepiece. Global from day one, with eleven rulesets at launch covering the Caribbean, Latin America, North America, and Europe. Free to play on iOS and Android, monetised via an optional premium subscription (ad removal + monthly token grant) and one-time token-pack IAPs. Built by INVOVIBE TECH LTD.

## Repo overview

| Path | Contents |
|---|---|
| [`unity/`](./unity/) | Unity 6 LTS client project (created interactively via Unity Hub — see bootstrap below) |
| [`functions/`](./functions/) | Firebase Cloud Functions (TypeScript, Node 20). The trust boundary: only thing that writes wallets / ELO / entitlements |
| [`docs/`](./docs/) | [`ARCHITECTURE.md`](./docs/ARCHITECTURE.md), [`PROJECT_BRIEF.md`](./docs/PROJECT_BRIEF.md), [`DECISIONS/`](./docs/DECISIONS/) (ADRs), [`RULES/`](./docs/RULES/) (per-variant specs) |
| [`firestore.rules`](./firestore.rules), [`firebase.json`](./firebase.json), [`.firebaserc`](./.firebaserc) | Firebase project + emulator config |
| [`scripts/`](./scripts/) | Build, deploy, codegen helpers |
| [`CLAUDE.md`](./CLAUDE.md) | Operating instructions for Claude Code sessions in this repo |

## Prerequisites

- **Unity 6 LTS** — install via [Unity Hub](https://unity.com/download). Add the Android Build Support and iOS Build Support modules.
- **Node.js 20** — required for Cloud Functions. [nvm](https://github.com/nvm-sh/nvm) (or [nvm-windows](https://github.com/coreybutler/nvm-windows)) recommended for version pinning.
- **Firebase CLI** — `npm install -g firebase-tools`. Then `firebase login`.
- **Java 17 JDK** — required by the Firebase emulator suite (Firestore + Auth emulators).
- **Photon Fusion 2 account** — sign up at [photonengine.com](https://www.photonengine.com/) and create three apps (dev, staging, prod). The Photon AppIDs are configured per Unity build variant — never committed.
- **Google Cloud / Firebase projects** — three projects (`dev-invovibe-dominoes`, `staging-invovibe-dominoes`, `prod-invovibe-dominoes`) aliased in [`.firebaserc`](./.firebaserc).
- **RevenueCat account** — created when M7 (monetization) work begins.

## Bootstrap

```bash
git clone <remote-url> posedominoes
cd posedominoes

# 1. Cloud Functions
cd functions
npm install
npm run build
npm test

# 2. Firebase emulator suite (from repo root)
cd ..
firebase emulators:start

# 3. Unity project
#   - Open Unity Hub
#   - Add project from disk → select the unity/ directory
#   - On first open, Unity creates Library/, Packages/, ProjectSettings/
#   - Use Unity 6 LTS (the exact LTS version pinned in docs/DECISIONS/0001-tech-stack.md)
```

## Where to start

- **Read [`CLAUDE.md`](./CLAUDE.md) first** if you're a new contributor or starting a Claude Code session — it documents conventions, trust boundaries, and the definition of done for any task.
- **Read [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md)** for the system design: components, trust model, match lifecycle, data model, anti-cheat strategy.
- **Read [`docs/PROJECT_BRIEF.md`](./docs/PROJECT_BRIEF.md)** for product context: who it's for, how it makes money, what's explicitly out of scope.
- **Decision log:** [`docs/DECISIONS/`](./docs/DECISIONS/) (ADRs, numbered).

## License

Proprietary © INVOVIBE TECH LTD. All rights reserved.
