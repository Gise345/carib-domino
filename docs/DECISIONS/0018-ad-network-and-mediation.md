# ADR 0018 — Ad network and mediation platform

- **Status:** Proposed — pending acceptance. No SDK imported, no code written.
- **Date:** 2026-08-17
- **Scope:** `functions`, `net`, `ui`, `docs`
- **Relates:** [0001](0001-tech-stack.md) (supersedes its ad-network choice
  only), [0016](0016-coin-economy-and-roster.md) (wallet trust boundary,
  `credit()`), [0017](0017-domino-slam.md) (rewarded grants are server-to-server)
- **Research:** [`docs/MONETIZATION_ADS.md`](../MONETIZATION_ADS.md)

## Context

ADR 0001 chose **"AdMob + LevelPlay (IronSource) mediation"** for free-tier ads.
That choice is now unbuildable as written, for two independent reasons.

**1. The ironSource Ads network no longer exists.** Unity shut it down on
30 April 2026, folding its demand into LevelPlay and Unity Ads and selling off
Supersonic. LevelPlay survives as a mediation platform, but "LevelPlay
(IronSource)" names a thing that is gone.

**2. LevelPlay pays net-60.** Payment lands roughly two months after the month
in which it was earned. Giselle has ruled net-60 out as a cash-flow constraint
on a bootstrapped launch; net-30 and net-15 are both acceptable. That removes
LevelPlay from consideration as the *mediator*.

ADR 0001 also phrased the choice as "AdMob **+** LevelPlay", which is ambiguous.
A publisher runs exactly one mediator. Everything else in the stack is a demand
source bidding into it.

## Decision (proposed)

**Google AdMob mediation at launch. Re-evaluate AppLovin MAX at scale.**

**The mediator is the payer.** Networks bidding inside a mediation stack have no
direct payment relationship with the publisher — the mediator collects all
revenue and pays out on its own terms. Only the mediator's net terms matter.
Consequence: Unity Ads demand can sit in the stack as a bidder without ever
exposing INVOVIBE to net-60. This is why the net-60 constraint eliminates
LevelPlay-as-mediator but does not eliminate Unity Ads as a demand source.

**Launch on AdMob** (net-30, $100 threshold). Rationale:

- No DAU minimum. AppLovin's AXON optimiser needs roughly 50–100k DAU to
  outperform simpler setups, and imposes a 60–90 day learning period. At launch
  this game has zero traffic, so MAX would actively underperform.
- Best global fill, and specifically the strongest fill in the Caribbean and
  LatAm markets that are the marketing focus.
- One SDK, one dashboard, one payment profile — the shortest path from zero to
  a first payout for a solo developer new to Unity.
- Google demand is the largest single Android source (25% of Android ad
  revenue share, Q2 2026).

**Migration trigger:** run a MAX A/B test when the game clears **~50k DAU or
~$5k/month ad revenue**, whichever comes first. AppLovin leads on raw rate,
especially on iOS (44% of iOS ad revenue share, Q2 2026), and pays net-15 —
better than AdMob on both counts, but only once there is enough traffic for its
optimiser to work. Do not migrate before the trigger.

**Insulate the choice behind `IAdService`.** A pure-C# interface under
`Core/`, with a thin MonoBehaviour adapter per SDK, so the AdMob → MAX
migration is a single adapter swap rather than a rewrite of every call site.
This follows the existing rule that game logic lives outside MonoBehaviours.

**Rewarded grants stay server-to-server.** Reaffirming ADR 0017: coins are
granted by a Cloud Function verifying AdMob's server-side verification (SSV)
signature, never by a client claim. `firestore.rules` already denies all client
writes to `wallets/{userId}`; the existing `credit()` helper in
`functions/src/wallet/wallet.ts` is the write path and currently has no caller.

## Scope of supersession

This ADR supersedes **only** the ad-network and mediation choice in ADR 0001.
Every other decision in 0001 — Unity 6, Photon Fusion 2, Firebase, RevenueCat
for IAP and subscription, DOTween, Addressables — is unchanged and still binding.

## Consequences

- Four files name the dead product and need correcting: `CLAUDE.md` (tech stack
  line), `docs/ARCHITECTURE.md` §10.3, `docs/PROJECT_BRIEF.md` (§"How it makes
  money" and the stack table), and ADR 0001.
- **New dependency, requires approval:** the Google Mobile Ads Unity plugin.
  Per `CLAUDE.md` this cannot be added without explicit sign-off.
- **New dependency, requires approval:** Firebase Remote Config
  (`Firebase.RemoteConfig.dll`), not currently in `unity/Assets/Firebase/`.
  Needed for ad frequency caps, reward sizing and a kill switch.
- **Trust boundary change, requires approval:** a new HTTPS Cloud Function
  endpoint for AdMob SSV. It is the first unauthenticated public endpoint in
  `functions/` and the first automated caller of `credit()`.
- iOS and Android both ship ads, so ATT (iOS), a UMP/CMP consent flow
  (GDPR/UK), Apple privacy manifests and the SKAdNetwork ID list in
  `Info.plist` all become launch blockers rather than polish.
- Accepting a lower ceiling at launch in exchange for a working stack sooner.
  The migration trigger is the mechanism that recovers the difference; if it is
  never acted on, this decision leaves money on the table at scale.
- Tokens must remain non-cashable and non-transferable. Adding cash-out or
  peer-transfer would reclassify the app under Google's Real-Money Gambling
  policy and jeopardise ad demand entirely. See the research doc.
