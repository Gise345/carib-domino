# ADR 0004 — Single Firebase project (carib-domino), promoted from staging to prod

- **Status:** Accepted
- **Date:** 2026-05-05
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Supersedes:** [`docs/ARCHITECTURE.md` §9.5](../ARCHITECTURE.md) (original three-project draft)

## Context

The original architecture draft proposed a standard three-environment Firebase setup: `dev-invovibe-dominoes`, `staging-invovibe-dominoes`, `prod-invovibe-dominoes`, each backed by its own Firebase project and aliased in [`.firebaserc`](../../.firebaserc). This is the canonical "dev / staging / prod" pattern that most enterprise SaaS shops use and that Firebase's own deployment guides assume.

That pattern is overkill for the actual operational reality of this project, and inconsistent with the founder's prior Firebase shipping experience.

The relevant facts:

1. **Solo developer through global launch.** No second engineer, no QA team, no separate staging environment for partner integrations.
2. **Pre-launch for ~5 months.** All infrastructure work between now and M10 (global launch) is essentially "dev" work — there is no audience yet for a true staging environment to serve.
3. **Founder has shipped multiple Firebase products** using a single-project pattern where the same project carries the app from internal alpha through public launch. That pattern has worked. Switching now would impose unfamiliar operational habits on the human in the loop without producing offsetting benefit.
4. **Firebase's primitives already support stage gating inside a single project**: Remote Config audiences, Cloud Functions staged rollouts, Firestore security rules versioning, and Auth provider toggles all let us treat one project as multi-stage at runtime rather than at infrastructure-provisioning time.
5. **The cost of the multi-project pattern is not zero**: three sets of credentials to manage, three sets of `google-services.json` / `GoogleService-Info.plist` files to keep in sync, three Firebase consoles to check, three Crashlytics dashboards, three Cloud Functions runtimes to deploy to, three places where configuration drift can hide. For a one-person team, that overhead compounds.

## Decision

Use **one** Firebase project with the ID **`carib-domino`** for the entire project lifecycle. Aliased as `default` in [`.firebaserc`](../../.firebaserc). The project starts in dev/staging mode and is promoted to prod when ready by progressively tightening configuration rather than by switching to a different project.

Concretely:

| Layer | Single-project handling |
|---|---|
| **Firestore rules** | One ruleset; gradually tighten as features mature. Dev-friendly defaults early; production-shaped rules at soft launch (M9). |
| **Cloud Functions** | One deployment target. From M9 onward, use staged rollouts (`firebase deploy --only functions:foo --force` with traffic split) to limit blast radius of new versions. |
| **Remote Config** | Gate unfinished or risky features behind audience conditions. Pre-launch: features default-on for the developer's own UID and default-off for everyone else. Post-launch: A/B test buckets and percentage rollouts. |
| **Auth providers** | One Auth instance. Anonymous always on; Apple/Google/email enabled when their respective M2/M9/M10 milestones arrive. |
| **Crashlytics + Analytics** | One project. Filter dashboards by build flavour or app version when needed. Build flavours (dev / release) write to the same project but tag events distinctly. |
| **Photon Fusion** | One Photon AppID for the same reasoning. AppID is environment-toggled in the Unity build only if multiple environments later prove necessary. |
| **RevenueCat** | One RevenueCat project. Sandbox vs. production receipts are handled by RevenueCat's own SDK based on store environment, not by switching projects. |

The `[.firebaserc](../../.firebaserc)` previously contained `dev`, `staging`, and `prod` aliases. As of this ADR it contains only `default → carib-domino`. The npm script `deploy:prod` is removed from [`functions/package.json`](../../functions/package.json) — `npm run deploy` now targets the only project there is.

## Rationale

1. **Operational fluency dominates.** The founder has shipped Firebase apps this way before and is fast in this configuration. Switching to multi-project would impose ongoing operational friction (which credential is loaded, which console am I looking at, which `.firebaserc` alias did I forget to swap) without producing a comparable benefit at this team size.
2. **Stage gating is a runtime concern, not an infrastructure concern.** Remote Config audiences and Cloud Functions traffic splitting let one project behave as multiple environments at the moments where multi-stage matters (rollouts, A/B tests, gradual feature exposure). Most of the value people attribute to "having a separate staging project" is actually delivered by these mechanisms within a single project.
3. **Configuration drift is the silent killer of multi-environment setups.** Three `google-services.json` files, three Firestore rule sets, three Remote Config templates — drift between any of these breaks correctness in subtle ways and is easy to introduce by accident, especially solo. One project means one source of truth.
4. **Reversibility is easy.** If the team grows or a true public-beta-in-parallel-to-private-alpha situation arises, re-introducing a `staging-carib-domino` project takes about an hour: create the project, add the alias to `.firebaserc`, add a `deploy:staging` npm script. No code refactor required because we never depended on multi-project structure in the first place.
5. **`.firebaserc` aliases are free.** This ADR removes the unused aliases for cleanliness, but if a future ADR re-introduces them, the cost of having added them is roughly zero.

## Consequences

**Positive**

- One credential, one console, one place to look. Lower operational tax.
- No drift surface across environments — by definition there is only one environment.
- Matches the founder's existing operational habits — fewer footguns specific to this codebase.
- Cleaner first-time-contributor onboarding: "the project is `carib-domino`, here's the URL to the Firebase console" rather than "there are three projects, which one are you trying to do, who has access to which."
- Reduced cost — Cloud Functions Blaze plan, Firestore reads, etc., are all metered against one project's free tier rather than diluted across three (the Blaze plan free tier is generous; this ordinarily wouldn't matter for cost, but for free tier testing it is helpful to have the entire allowance against one project).

**Negative / accepted trade-offs**

- **No isolated staging.** A risky migration or a malformed Remote Config change affects the live (or about-to-be-live) project directly. Mitigations: Cloud Functions staged rollouts; Remote Config audience gating to the developer's UID for unfinished work; Firestore rule changes tested locally via the emulator suite before deploy; database schema migrations versioned and forward-compatible by design.
- **No isolated prod.** Once we have real users (M9 onward), every Firestore read in development counts against the same quota the live users are using. Acceptable at our cost projections (target <$2k/month at 100k MAU per [`docs/ARCHITECTURE.md`](../ARCHITECTURE.md) section 9.4); Firestore quotas are nowhere near a constraint for a solo developer's local activity.
- **Anti-cheat and admin tooling can't be tested against a "clean" project.** Every test of the settlement validator, RNG seed pipeline, or webhook handler runs against the same dataset live users will touch. Mitigations: emulator suite for end-to-end testing locally; admin Cloud Functions are gated behind a custom auth claim (`admin: true`) that no public user has; admin tooling writes to a `_admin/` collection prefix that production rules deny to non-admin reads.
- **Reverting this decision is harder than introducing it would have been originally.** If we ever need to split out staging, we'll have to migrate Firestore data, re-issue Photon AppIDs, re-link RevenueCat — a half-day's work versus the ten minutes it would take to do the split today. Acceptable given the high probability we never need to revert it.

## Alternatives considered

- **Three projects from day one (the original architecture draft).** Rejected on the grounds above: imposes ongoing operational friction without offsetting benefit at one-engineer scale, and is inconsistent with the founder's prior shipping experience.
- **Two projects: dev + prod, no staging.** A reasonable middle ground. Rejected because dev and prod are still two consoles to manage, two sets of credentials, and the boundary between them blurs in solo work — "dev" effectively becomes "anything I'm doing right now" which is the same as one project. If a true production project later needs to be cleanly separated, ADR 0004 can be revisited; the cost of starting at one and splitting at M9 or M10 is bounded.
- **Multiple Firebase projects, one Photon AppID, one RevenueCat project.** A hybrid pattern. Rejected because the same arguments that justify single Firebase also apply to Photon and RevenueCat — operational fluency and configuration consistency dominate.

## References

- [`.firebaserc`](../../.firebaserc) — the single-alias config this ADR pins.
- [`docs/ARCHITECTURE.md` §9.5](../ARCHITECTURE.md) — environments section, updated to reflect this decision.
- [`README.md`](../../README.md) — prerequisites updated to mention one Firebase project.
- [`functions/package.json`](../../functions/package.json) — `deploy:prod` script removed.
- ADR [`0001-tech-stack.md`](0001-tech-stack.md) — original tech stack decision (this ADR refines its environment posture).
