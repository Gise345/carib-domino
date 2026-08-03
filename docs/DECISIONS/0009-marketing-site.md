# ADR 0009 — Marketing site on Firebase Hosting, tester signups via Cloud Function

- **Status:** Accepted
- **Date:** 2026-08-03
- **Deciders:** Giselle Johnson (Founder/CEO/CTO, INVOVIBE TECH LTD)
- **Relates to:** ADR 0004 (single Firebase project)

## Context

The 1 September soft launch needs a public face: somewhere to explain what the
closed test is, publish the Jamaica ruleset, signal that Cuban / Mexican and the
rest of the catalogue come later, and collect a bounded pool of testers for
Google Play internal testing and Apple TestFlight.

The domain `caribbeandominos.com` is registered at Hostinger (DNS only — no
hosting product in use). `caribbeandominoes.com`, the brand spelling, was already
taken by a third party; `Pose-Dominoes` is to be acquired later, at which point
one domain will 301 to the other.

Two questions needed deciding: where the site lives, and how tester emails get
captured without opening a client write path into Firestore.

## Decision

### 1. The site is static HTML/CSS/JS on Firebase Hosting

`web/public/` deploys to the existing `carib-domino` project (ADR 0004 — one
project). No framework, no build step, no new dependencies. It is a single
scrolling page plus `privacy`, `terms`, and a 404.

Rationale: the page is a teaser with one form on it. A framework would add a
toolchain and a `node_modules` tree to maintain for no capability we need, and
Hosting is already paid for and already in the deploy story.

Custom-domain DNS points `@` and `www` at Firebase's A records with the
Hostinger-default `A`/`CNAME` records removed. `MX` and mail `TXT` records stay
untouched.

### 2. Signups go through a Cloud Function, never a client Firestore write

`testerSignup` is an `onRequest` HTTPS function exposed at `/api/tester-signup`
through a Hosting rewrite, so it is same-origin with the site and CORS stays off.
It Zod-validates the payload and writes with the Admin SDK to
`testerSignups/{sha256(normalised email)}`. Firestore rules deny that collection
to every client, read and write.

Rationale: the alternative — a narrow create-only rule and the web SDK in the
browser — would have put a publicly writable path into the same Firestore that
holds wallets and match state. That is the exact thing `CLAUDE.md`'s trust
boundary rule exists to prevent, and the convenience saved is one function.

The site ships **no Firebase SDK at all**. It has no API key, no project config,
and no Firestore credentials of any kind. Its entire backend surface is one POST
endpoint.

### 3. Abuse controls are deliberately light

- **Honeypot field** (`nickname`) — hidden from people by CSS, filled by naive
  bots. A tripped honeypot returns `200 {ok:true}` and writes nothing, so the bot
  gets no signal to tune against.
- **Deterministic document ID** from the hashed email — a resubmission merges
  into the existing record instead of taking a second seat, so flooding one
  address cannot inflate the pool.
- **`maxInstances: 3`** caps the blast radius (and the bill) of a flood.
- **Body-size and field-length caps** before and inside validation.

No App Check, no CAPTCHA, no per-IP rate limit. For a four-week closed test
advertised to a small audience, those cost more in friction and setup than the
spam they would prevent. If the pool gets poisoned, App Check on the endpoint is
the next step.

### 4. Rules copy is generated from the implementation, not from memory

The Jamaica rules section documents what `functions/src/rules/` actually does —
no boneyard, highest double leads, block when all four pass consecutively,
team-based domino/block/resign scoring, disconnect-as-resign per ADR 0008. Match
target score is deliberately **not** stated, because none is implemented yet.

## Consequences

**Positive**
- No client-writable Firestore path is introduced; the trust boundary holds.
- No new dependencies in the repo, and no second deploy target.
- Tester contact details are unreadable by any client, which is the correct
  posture for personal data.
- The site is cheap and fast — static assets, an immutable-cached image set, and
  one cold-startable function that only runs on form submit.

**Negative / accepted trade-offs**
- **The domain is the wrong spelling.** `caribbeandominos.com` will be typo'd as
  `dominoes` by anyone who knows the brand. Mitigation is deferred to acquiring a
  better domain later and redirecting.
- **No rate limiting.** A determined actor can burn function invocations up to
  the `maxInstances` cap. Accepted for a short closed test; App Check is the
  escalation.
- **Copy is hand-maintained.** The rules section will drift if the engine changes
  and nobody updates the page. It is marketing copy, not a spec — the per-variant
  specs in `docs/RULES/` remain the source of truth once written.
- **Legal pages are not lawyer-reviewed.** They describe actual practice in plain
  language and say so explicitly. Full terms and a privacy policy are required
  before general release regardless.

## References

- `web/public/` — the site.
- `functions/src/web/testerSignup.ts` — the endpoint.
- `functions/test/web/testerSignup.test.ts` — validation, dedup, honeypot, method
  and failure coverage.
- `firestore.rules` — `testerSignups` deny-all block.
- `firebase.json` — hosting config, `/api/tester-signup` rewrite, CSP and cache
  headers.
