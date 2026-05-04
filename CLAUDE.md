# CLAUDE.md

This file gives Claude Code persistent context for every session in this repo. Read it carefully on session start. The architecture document (`docs/ARCHITECTURE.md`) is the authoritative source for system design — refer to it whenever a question affects component boundaries, data model, or trust model.

---

## Project

A free-to-play multiplayer dominoes mobile game. Caribbean rulesets are the marketing focus, but eleven rulesets ship globally at launch. See `docs/PROJECT_BRIEF.md` for the product overview and `docs/ARCHITECTURE.md` for the system design.

## Tech stack — non-negotiable

- **Game client:** Unity 6 LTS, C#, .NET Standard 2.1
- **Realtime multiplayer:** Photon Fusion 2
- **Backend:** Firebase (Auth, Firestore, Cloud Functions, Remote Config, Crashlytics, Analytics, Cloud Messaging)
- **Cloud Functions runtime:** Node.js 20, TypeScript
- **Subscription/IAP:** RevenueCat
- **Ads:** Google AdMob + LevelPlay (IronSource) mediation
- **Animation:** DOTween (free) + Unity's built-in animator
- **UI text:** TextMeshPro everywhere — never legacy `UI.Text`
- **Asset delivery:** Unity Addressables
- **Localization:** Unity Localization Package, with remote string tables in Firestore

If a task seems to call for a different tool, stop and ask before introducing it. New dependencies need explicit approval.

## Repo layout

```
.
├── unity/                         # Unity project root
│   ├── Assets/
│   │   ├── _Project/              # All project-specific assets (underscore sorts to top)
│   │   │   ├── Scripts/
│   │   │   │   ├── Core/          # Pure C#, no Unity dependencies — testable
│   │   │   │   │   ├── Rules/     # Rule engine (canonical for client)
│   │   │   │   │   ├── State/     # Match state, immutable where possible
│   │   │   │   │   └── Utils/
│   │   │   │   ├── Game/          # MonoBehaviours, scene controllers
│   │   │   │   ├── UI/            # UI controllers and views
│   │   │   │   ├── Net/           # Photon and Firebase wrappers
│   │   │   │   └── Bootstrap/     # App init, dependency wiring
│   │   │   ├── Scenes/
│   │   │   ├── Prefabs/
│   │   │   ├── Art/
│   │   │   ├── Audio/
│   │   │   └── Localization/
│   │   ├── Plugins/               # Third-party SDKs
│   │   └── Tests/
│   │       ├── EditMode/          # Unit tests, no Play mode required
│   │       └── PlayMode/          # Integration tests in Unity runtime
│   ├── Packages/                  # UPM manifest
│   └── ProjectSettings/
│
├── functions/                     # Firebase Cloud Functions (TypeScript)
│   ├── src/
│   │   ├── matchmaking/
│   │   ├── settlement/            # Match log validation & wallet writes
│   │   ├── rules/                 # Rule engine (canonical for server)
│   │   ├── webhooks/              # RevenueCat, etc.
│   │   ├── admin/                 # Internal-only callable functions
│   │   └── lib/                   # Shared utilities, types
│   ├── test/
│   ├── package.json
│   └── tsconfig.json
│
├── firestore.rules                # Security rules
├── firestore.indexes.json
├── firebase.json
├── .firebaserc
│
├── docs/
│   ├── ARCHITECTURE.md            # System design — read first
│   ├── PROJECT_BRIEF.md           # Product overview
│   ├── DECISIONS/                 # ADRs (one .md per decision)
│   └── RULES/                     # Per-variant rule specifications
│
├── scripts/                       # Build, deploy, codegen helpers
├── .gitignore
├── README.md
└── CLAUDE.md                      # This file
```

## How to work in this repo

### Working principles

1. **Trust boundary discipline.** Anything that affects a wallet, ELO, stats, entitlement, or match outcome can ONLY be written by Cloud Functions. Never add a Firestore client write to these paths. If a task seems to require it, stop — the task is wrong.
2. **Rule engine parity.** The C# rule engine in `unity/Assets/_Project/Scripts/Core/Rules/` and the TypeScript rule engine in `functions/src/rules/` must produce identical results for the same inputs. Any change to one requires a corresponding change to the other and a passing replay-log fixture test.
3. **Server-issued RNG seeds always.** Tile shuffles use a deterministic PRNG seeded by the Cloud Function. Never `Random.Range`, never `Math.random()`, never `new Random()` for gameplay-affecting randomness. Cosmetic-only randomness (particle directions, idle animation timing) is fine with default RNG.
4. **No game logic in MonoBehaviours.** Game logic lives in pure C# classes under `Core/`. MonoBehaviours are thin adapters that wire input, networking, and rendering to the core. This is what makes the rule engine testable.
5. **Async/await, never coroutines for async work.** Use `UniTask` (add as a dependency when needed) for Unity-aware async. Coroutines are reserved for frame-locked animation sequences only.
6. **Addressables for everything dynamic.** Tile skins, board themes, avatars, sound packs all load via Addressables. Never `Resources.Load`.
7. **Localization keys for every user-facing string.** No hardcoded English strings in UI. Even for prototypes, use `LocalizationSettings.StringDatabase.GetLocalizedString("Key")` or a `LocalizeStringEvent` component.

### Coding conventions

**C# (Unity client)**
- Microsoft conventions: `PascalCase` for public, `_camelCase` for private fields, `camelCase` for locals/parameters.
- One type per file, filename matches type name.
- `readonly` everywhere it's correct. Prefer immutable data classes for state objects.
- Nullable reference types enabled (`#nullable enable` at file top until project-wide setting flips).
- No `var` for primitive types; `var` is fine when the type is obvious from the right-hand side.
- XML doc comments on public APIs. Inline comments only for "why," never "what."
- Tests use NUnit (Unity's built-in Test Framework). One test class per production class. AAA structure (Arrange/Act/Assert) with blank lines between phases.

**TypeScript (Cloud Functions)**
- ESLint + Prettier. Config in `functions/.eslintrc.json`.
- `strict: true`, `noUncheckedIndexedAccess: true` in tsconfig.
- `interface` for object shapes, `type` for unions and aliases.
- No default exports — named exports only.
- Tests use Vitest. Co-locate `*.test.ts` next to source.
- All exported functions have JSDoc with `@param` and `@returns`.
- All Cloud Function entrypoints validate input with Zod schemas before any logic runs.

**Firestore security rules**
- Default deny. Every collection needs an explicit allow rule.
- Wallet, stats, ELO, entitlement, match-result paths: client read-own, no client write.
- Use `request.auth.uid` for ownership checks, never trust document fields for identity.
- Test rules with the Firebase emulator before every deploy.

### Commits

Conventional Commits. Examples:
- `feat(rules): add Cuban double-9 scoring`
- `fix(net): correct turn-timer reset on host migration`
- `refactor(ui): extract HandView from BoardScene controller`
- `test(rules): add replay fixtures for capicú edge cases`
- `chore(deps): bump Photon Fusion to 2.x.y`
- `docs(architecture): clarify trust boundary 4`

Scope is the subdirectory: `rules`, `net`, `ui`, `core`, `functions`, `infra`, `docs`, etc. Keep commits focused and reviewable. Don't combine unrelated changes.

### Branching

- `main` — always deployable to staging
- `develop` — integration branch (only if needed; flat history off `main` is preferred for solo work)
- `feat/<short-name>` — feature branches
- `fix/<short-name>` — fixes
- Squash-merge to `main`

### When to ask vs. when to proceed

**Proceed without asking when:**
- The task is clearly defined and you have all the information needed
- The change is local in scope (one or two files in the same module)
- There's a clear convention from this file or `docs/ARCHITECTURE.md` to follow
- You're writing tests for code that already exists

**Stop and ask when:**
- The task requires a new dependency
- The task crosses a trust boundary or changes a Firestore security rule
- The task requires changing a published API (RPC contract, Photon networked struct, Firestore document shape)
- The task seems to conflict with `docs/ARCHITECTURE.md`
- You'd be making a decision that future sessions or the human will need to live with (record it in `docs/DECISIONS/` if you proceed)
- The estimated change is more than ~300 lines or touches more than ~5 files

### Definition of done for any task

A task is not done until:
1. Code compiles cleanly (zero warnings in changed files; existing warnings are not your problem unless explicitly in scope)
2. Tests exist and pass (unit at minimum; integration where the change crosses module boundaries)
3. Lint passes (`npm run lint` for functions, Unity Roslyn analyzers for C#)
4. The change is committed with a Conventional Commits message
5. If a public API or schema changed, `docs/ARCHITECTURE.md` reflects it (or an ADR in `docs/DECISIONS/` records the change)
6. If user-facing, every new string has a localization key

### Anti-patterns to refuse

- Disabling tests to "make it pass"
- Adding `try/catch` blocks that swallow errors silently
- Magic numbers — extract to named constants
- Long methods (>40 lines is a smell, refuse >80 without an extraction plan)
- Singletons for application state (use a service locator or DI container; `ServiceLocator.Instance.Resolve<T>()` is fine)
- `// TODO` comments without an associated tracking issue or ADR
- Committing secrets, API keys, or `.env` files (already in `.gitignore`)
- Changing the rule engine on one side (C# or TS) without updating the other and re-running shared fixtures

## Common commands

```bash
# Cloud Functions
cd functions
npm install
npm run build              # tsc
npm run lint               # eslint
npm test                   # vitest
npm run serve              # firebase emulators:start --only functions
npm run deploy             # firebase deploy --only functions (staging)
npm run deploy:prod        # firebase deploy --only functions --project prod-invovibe-dominoes

# Firestore
firebase emulators:start   # full local emulator suite
firebase deploy --only firestore:rules

# Unity
# Open via Unity Hub. Builds via Unity Cloud Build (configured separately) or local CLI:
# /Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity \
#   -batchmode -nographics -projectPath ./unity -buildTarget Android \
#   -executeMethod BuildScript.BuildAndroid -quit
```

## Environment files

- `.env.local` (gitignored) — local Cloud Functions emulator overrides
- `unity/Assets/_Project/Settings/EnvironmentConfig.asset` — Unity-side environment selector (dev/staging/prod). Switches Firebase project and Photon AppID.
- `firebase.json` — multi-project config; `.firebaserc` aliases `dev`, `staging`, `prod`.

Never commit credentials. Service account JSON files go in `~/.config/invovibe/` outside the repo and are referenced by absolute path in tooling.

## Pointers

- System architecture: `docs/ARCHITECTURE.md`
- Product overview: `docs/PROJECT_BRIEF.md`
- Per-variant rule specs: `docs/RULES/{variant}.md` (created as variants are implemented)
- Decision log: `docs/DECISIONS/NNNN-title.md` (Architecture Decision Records, numbered)

## Personal context

The human you're working with is Giselle Johnson — full-stack engineer, founder/CEO/CTO of INVOVIBE TECH LTD, based in Cayman Islands. Highly experienced with React Native/Expo, Firebase, TypeScript. New to Unity and C#. AI-assisted engineering, not vibe coding — favours quality, structure, and testable code over speed. When introducing Unity-specific concepts (e.g. ScriptableObject, Addressables groups, prefab variants), include a brief one-line explanation the first time per session.
