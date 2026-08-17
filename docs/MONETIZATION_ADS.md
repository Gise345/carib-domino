# Ad monetization — research and setup plan

- **Date:** 2026-08-17
- **Status:** Research. Decisions recorded in
  [ADR 0018](DECISIONS/0018-ad-network-and-mediation.md) (Proposed).
- **Milestone:** Ads are **M7** per `ARCHITECTURE.md` §12. Nothing ad-related
  exists in the codebase today.
- **Platforms:** Google Play **and** App Store.

---

## 1. Which company pays the best?

Short answer: **AppLovin has the highest ceiling, AdMob is the right first
account, and the mediator you pick matters less than your player geography.**

The question "which network pays most" has a slightly wrong shape, because
serious publishers do not pick a network — they pick a **mediator**, which runs
a real-time auction across many networks for every single ad impression.
Mediation raises revenue roughly **20–35%** over any single network, because
networks bid against each other instead of you accepting one network's price.[^liftoff]

So the real question is: which mediator, and who bids inside it.

### The three candidates

| | **Google AdMob** | **AppLovin MAX** | **Unity LevelPlay** |
|---|---|---|---|
| Raw eCPM | Good | **Best**, esp. iOS | Good |
| Market share | 25% Android[^tenjin] | **44% iOS**, 23% Android[^tenjin] | Smaller |
| Minimum traffic | **None** | ~50–100k DAU to perform[^segwise] | None |
| Payment terms | net-30[^admobpay] | **net-15**[^applovinpay] | net-60[^unitypay] |
| Payout threshold | $100 | $100 | $100 |
| Setup difficulty | **Lowest** | Highest | Low (Unity-native) |
| Fill in LatAm/Caribbean | **Best** | Good | Good |

**AppLovin** genuinely leads on rate. Tenjin's Q2 2026 benchmark puts it at 44%
of all iOS ad revenue (up from 39%), and 23% on Android just behind AdMob's
25%.[^tenjin] Its AXON optimiser is the reason.

**But AXON is also why AppLovin is the wrong launch choice.** It needs data to
optimise. Below ~50k DAU there aren't enough data points, and publishers should
expect a **60–90 day learning period with a temporary revenue dip** while it
calibrates.[^segwise] Launching a zero-traffic game on MAX means paying that
cost for a benefit you can't yet collect.

**Unity LevelPlay is eliminated by cash flow, not quality.** See §2.

### Recommendation

**Open an AdMob account. Launch on AdMob mediation. Re-evaluate AppLovin MAX
once the game clears ~50k DAU or ~$5k/month.**

Build the client behind an `IAdService` interface so that migration is a single
adapter swap, not a rewrite.

---

## 2. Payment terms, and the rule that decides this

**"Net 30" is a payment deadline** — the money is due 30 days after the billing
period closes. It says nothing about *how much* you earn, only *when it lands*.

For January's earnings:

| Mediator | Terms | Money arrives |
|---|---|---|
| AppLovin MAX | net-15 | ~15 February |
| Google AdMob | net-30 | ~21 February |
| Unity LevelPlay | net-60 | ~31 March |

All three hold funds until the balance reaches **$100**; below that it rolls
into the next month.

### The mediator is the payer

This is the fact that settles the LevelPlay question, and it is worth writing
down so it isn't re-litigated later:

> Networks bidding inside a mediation stack have **no direct payment
> relationship** with the publisher. The mediator collects all revenue and pays
> you on its own terms. Only the **mediator's** net terms matter.

Consequence: Unity Ads demand can sit inside an AdMob or MAX stack and bid on
your impressions, and you still get paid on net-30 or net-15. Net-60 only
applies if **LevelPlay itself is the mediator**.

Since net-60 is ruled out and net-30/net-15 are acceptable, LevelPlay is out as
the mediator — but Unity Ads remains perfectly usable as a demand source.

This contradicts `CLAUDE.md`, `ARCHITECTURE.md` §10.3, `PROJECT_BRIEF.md` and
ADR 0001, which all specify LevelPlay mediation. See §9.

---

## 3. What you'll actually earn

### eCPM benchmarks

**Read these as order-of-magnitude, not forecast.** These are third-party
aggregates from monetization blogs and vendor marketing, not audited data.
Tenjin's report — the most credible source found — publishes its eCPM figures
as chart images with no extractable numbers, so the tables below are assembled
from secondary sources. **Your own first 30 days of AdMob data will be worth
more than every number here.**

eCPM = effective cost per *mille* = revenue per 1,000 ad impressions.

**By format (rewarded video is the best-paying format, and it's what you want
for free tokens):**

| Format | Tier-1 (US/UK/JP) | Tier-2/3 |
|---|---|---|
| Rewarded video | $15–40[^coinis] | $3–10[^coinis] |
| Interstitial | ~$15–20[^udonis] | low single digits[^udonis] |
| Banner | Lowest of the three | Lowest |

**By region — this is the number that matters most for this game:**

| Region | Rewarded eCPM, Android |
|---|---|
| United States | ~$16.49 (iOS ~$19.63)[^coinis] |
| Top-20 countries | $3.31 – $12.91[^coinis] |
| **LatAm / Caribbean** | **~$2–4**[^udonis] |
| Southeast Asia | ~$2[^udonis] |

### The geography problem — read this twice

**A Caribbean-marketed game earns roughly 5x more per ad from a player in
Brooklyn than from a player in Kingston.**

US rewarded video runs ~$16.49/1,000 impressions. Caribbean and LatAm runs
~$2–4. Same ad, same game, same player behaviour — 5x the revenue, purely from
where they live.

This is a bigger lever than the AdMob-vs-AppLovin choice, and it has direct
consequences:

- **The Caribbean diaspora is the monetizing audience.** Players in the US, UK,
  Canada and Toronto are the ad revenue base. In-region players are the
  cultural authenticity, the word-of-mouth, and the reason the game is good —
  but they are not where ad revenue comes from.
- **Weight UA spend toward diaspora markets.** A US install can be worth 5x a
  regional install in ad revenue, which changes what you can afford to pay for
  it.
- **Ad revenue projections in `ARCHITECTURE.md` §10 should be modelled on a
  geo-weighted blend**, not a single global eCPM. A blended average assuming
  tier-1 rates against a majority-regional player base will overstate revenue
  by several times.
- **Subscription and IAP matter proportionally more in-region**, because ads
  monetize regional players so weakly.

---

## 4. iOS vs Android

Both stores are in scope, and they behave differently.

- **iOS earns more per impression** — US rewarded is ~$19.63 on iOS vs ~$16.49
  on Android.[^coinis] Android carries slightly more total revenue (55% vs 45%)
  purely on volume.[^tenjin]
- **ATT opt-in has plateaued at ~27% globally** — US 31%, EU 22%, JP 38%.[^adlib]
  It has not moved since 2021 and will not.
- **Unconsented traffic earns 20–40% lower eCPM** because targeting signals are
  missing.[^coinis2] With ~73% of iOS users declining, most of your iOS
  inventory is unconsented.
- A well-designed pre-permission explainer before the ATT prompt measurably
  raises opt-in.[^coinis2] Worth doing properly — it is one of the few levers
  that directly moves iOS eCPM.

### Launch blockers this creates

Because both stores ship ads, these stop being polish and become launch
requirements:

- **ATT prompt** (iOS) with a value-exchange explainer screen beforehand.
- **UMP / CMP consent flow** for GDPR and UK users — Google's User Messaging
  Platform ships with the Mobile Ads SDK.
- **Apple privacy manifests** (`PrivacyInfo.xcprivacy`) for the app and every
  ad SDK.
- **SKAdNetwork ID list** in `Info.plist` — a long list supplied by the
  mediator, needed for iOS attribution.
- **`app-ads.txt`** published on the marketing domain, declaring AdMob as an
  authorised seller. Without it a meaningful slice of programmatic demand
  refuses to bid.

---

## 5. The three placements

### 5.1 Interstitial when a game ends

**Trigger:** when a game concludes and a winner is declared. In cut-throat, the
ad plays once someone has won the game — not after every round.

**Where it hooks:** `unity/Assets/_Project/Scripts/Game/BoardBootstrap.cs`
already has the exact seam. `OverlayMode` (~line 358) distinguishes
`RoundOver`, `OpponentLeft` and `MatchOver`, and `RefreshEndOverlay(MatchState)`
(~line 2062) is the single funnel that decides which end state to show. The ad
gates on **`MatchOver`**, never `RoundOver`. `EndOverlayView.cs` is the widget
itself.

**Conflict to resolve:** `ARCHITECTURE.md` §10.3 currently specifies
"Interstitial after every 3rd match". Giselle's instruction is after every game.
These disagree.

**Recommendation:** ship every-game as the **default value in Remote Config**,
not a hard-coded constant. Interstitial frequency is the single most
retention-sensitive number in the whole monetization design, and if D1/D7 drops
you want to dial it back that afternoon — not ship a store update and wait for
review. Same code either way; only the default differs.

**Non-negotiable:** never during ranked. `ARCHITECTURE.md` §10.3 is right about
this and it should not be softened. Interrupting a staked competitive match is
the fastest way to lose the players who matter most.

### 5.2 Rewarded video for free tokens

**Design:** daily-capped, server-enforced. Suggested starting point: **3–5
views per day**, each granting a fraction of the entry stake.

**Sizing constraint:** `functions/src/lib/economy.ts` sets `ENTRY_STAKE =
1_000`, `STARTING_COINS = 10_000`, `KEY_BONUS = 2_000`. Token packs run
$1.99–$49.99 for 2,500–100,000 tokens (`ARCHITECTURE.md` §10.1). Rewarded
grants must be small enough that grinding ads is meaningfully worse than buying
a pack, or the offer cannibalises IAP — which is the higher-margin revenue.

**The cap must be enforced server-side.** A client-side daily counter is a
`PlayerPrefs` integer, which is to say it is free coins for anyone with a rooted
phone.

**Uncapped rewarded video was considered and rejected.** It maximises ad
revenue per user while destroying both the token packs and the meaning of the
stake economy.

### 5.3 Lobby banner

As already specified in `ARCHITECTURE.md` §10.3 — lobby only, never during a
match. Lowest eCPM of the three formats but it costs nothing in attention
because the player is already idle.

### 5.4 Ad removal

Paid subscription removes ads. Giselle floated **$1.99/month**. Note that
`PROJECT_BRIEF.md` and `ARCHITECTURE.md` §10.1 currently specify a **$4.99/month**
premium tier that removes ads *and* grants 5,000 tokens monthly. These are
different products at different prices — see §8.

Per §10.3, subscription removes all placements **except optional rewarded
videos**, which subscribers should keep access to. Rewarded video is opt-in by
definition; removing it takes a benefit away rather than removing an annoyance.

---

## 6. Trust boundary — rewarded grants must be server-to-server

This is the part that must not be got wrong.

[ADR 0017](DECISIONS/0017-domino-slam.md) already states the rule:

> "Granting on ad-completion goes through the ad network's server-side reward
> callback, never a client claim."

`ARCHITECTURE.md` §7 Boundary 3 is the general form: Cloud Functions are the
sole writer to anything a player cares about.

**Why it matters here specifically:** if the client tells the server "I watched
an ad, give me coins," then a modified client says that every 200ms and mints
unlimited currency. In a game where coins gate entry to staked matches, that is
not a cosmetic exploit — it corrupts the competitive economy.

**The correct shape:**

```
Player finishes rewarded ad
  → AdMob's servers call your HTTPS endpoint directly (SSV)
    → Cloud Function verifies AdMob's cryptographic signature
      → checks the transaction ID hasn't been seen before (replay guard)
        → checks today's grant count against the server-side cap
          → calls credit() in functions/src/wallet/wallet.ts
            → client re-reads its balance via getWallet
```

The client is never in the grant path. It only *observes* that its balance
changed.

**What already exists:** `credit()` in `functions/src/wallet/wallet.ts` — a
transactional `FieldValue.increment`, currently with **no caller**.
`firestore.rules` already denies all client writes to `wallets/{userId}`:

```
match /wallets/{userId} {
  allow read: if request.auth != null && request.auth.uid == userId;
  allow write: if false;
}
```

**No security-rules change is needed.** The boundary is already correct.

**What needs approval:** the SSV endpoint would be the **first unauthenticated
public HTTPS function** in `functions/` and the first automated caller of
`credit()`. Per `CLAUDE.md`, a trust-boundary change requires explicit sign-off
before implementation.

---

## 7. What has to be built before an ad can show

Nothing ad-related exists yet. In rough dependency order:

1. **AdMob account + app registration + payment profile.** Giselle's task; I
   can't do this. Everything else waits on the app IDs it produces.
2. **Google Mobile Ads Unity plugin** — a new dependency. `CLAUDE.md` requires
   explicit approval.
3. **Firebase Remote Config** — `Firebase.RemoteConfig.dll` is **not** in
   `unity/Assets/Firebase/Plugins/` and there are zero `RemoteConfig`
   references in any `.cs` file. Needed for ad frequency, reward sizing, and an
   ad kill switch. Wire it in `Net/FirebaseBootstrap.cs`.
4. **`IAdService`** — pure C# interface under `Core/`, MonoBehaviour adapter in
   `Net/` or `Game/`. Keeps game logic out of MonoBehaviours per `CLAUDE.md`,
   and makes the eventual MAX migration a one-adapter change.
5. **Consent layer** — ATT prompt, pre-permission explainer, UMP/CMP.
6. **SSV Cloud Function** — signature verification, replay guard, daily cap,
   calls `credit()`. Trust-boundary approval required.
7. **Entitlement check** — `entitlements/{userId}` gates whether ads show at
   all. Depends on the RevenueCat pipeline, which is also unbuilt.
8. **Placement wiring** — `MatchOver` in `BoardBootstrap.cs`, lobby banner,
   rewarded entry point in the shop/lobby.
9. **Localization keys** for every new user-facing string — "Watch an ad for
   500 coins", "Remove ads", the ATT explainer. `CLAUDE.md` forbids hardcoded
   English.

---

## 8. Regulatory position — a constraint to protect

`PROJECT_BRIEF.md` states tokens are "explicitly play-money: no real-world
value, no withdrawal, no peer transfer. This keeps the product cleanly outside
gambling regulation everywhere."

**This is correct and it is load-bearing. Do not weaken it.**

Google spent 2025–26 tightening exactly this area. In October 2025 it removed
sweepstakes casinos from the social-casino certification category specifically
because they let players redeem virtual currency for real-world value, pushing
them under the far stricter online-gambling regime requiring operator licensing
and per-country certification.[^igaming][^bsn] Further certification changes
landed August 2026.[^googleads]

Carib Domino stays outside all of this **only** while tokens are non-cashable
and non-transferable. Adding cash-out, prize redemption, or player-to-player
transfer would:

- reclassify the app under Google Play's Real-Money Gambling policy,[^playpolicy]
- require licensing and per-country certification,
- restrict which advertisers will bid on your inventory, cutting eCPM,
- and put App Store distribution at risk in most territories.

The staked-match structure — 1,000-coin entry, winner takes the pot — is fine
*because the coins are worthless outside the game*. That is the entire
distinction. Protect it.

---

## 9. Doc drift found during this research

Four files specify a product that no longer exists, and two contain figures
that contradict the shipped code:

| File | Issue |
|---|---|
| `CLAUDE.md` | Tech stack: "Google AdMob + LevelPlay (IronSource) mediation" |
| `docs/ARCHITECTURE.md` §10.3 (~line 528) | Same, plus "every 3rd match" vs the every-game instruction |
| `docs/PROJECT_BRIEF.md` (~lines 27, 44) | Same, in prose and the stack table |
| `docs/DECISIONS/0001-tech-stack.md` | Same (~lines 29–30, 60–66, 87) |
| `docs/ARCHITECTURE.md` §10.2 | "Casual: 50 tokens entry, 90-token reward" — code uses a flat 1,000 stake |
| `docs/ARCHITECTURE.md` §10 data model | Field named `tokens`; implementation uses `coins` |

Not corrected in this pass — they are stack-level files and ADR 0018 is still
Proposed. Correct them when it's accepted.

---

## 10. Open questions

1. **Subscription pricing.** $1.99/month ad-removal vs. the $4.99/month premium
   tier already in the docs — is $1.99 a new cheaper ads-only SKU alongside
   $4.99, a replacement for it, or was it illustrative? Two SKUs means two
   RevenueCat entitlements and an ad-gating check that reads both. A
   replacement drops the token-grant revenue. Either way it needs its own ADR.
2. **Rewarded reward size and daily cap.** Can't be finalised until the §10.2
   vs ADR 0016 stake discrepancy is resolved.
3. **Interstitial default frequency.** Recommendation is Remote Config with
   every-game as the default; confirm before wiring.
4. **Offerwall.** Deferred. Offerwall eCPMs are dramatically higher than
   rewarded video, but it's a separate integration with its own fraud surface
   and callback. Worth revisiting after the base rewarded flow is proven.

---

## 11. AdMob account setup checklist

For when you're ready to open the account:

- [ ] Create AdMob account (use the INVOVIBE Google account, not a personal one)
- [ ] Set up the payment profile — bank details, tax forms. Do this early;
      verification takes time and blocks your first payout, not your first ad.
- [ ] Register both apps (Android + iOS) — even pre-launch, using the
      "not yet published" option
- [ ] Create ad units: `interstitial_match_end`, `rewarded_coins`,
      `banner_lobby` — one set per platform
- [ ] Enable server-side verification on the rewarded unit and note the
      verification key URL
- [ ] Publish `app-ads.txt` on the marketing domain
- [ ] Register test device IDs — **never** click your own live ads; AdMob bans
      accounts for it and the ban is usually permanent
- [ ] Configure UMP consent messages for GDPR/UK in the AdMob dashboard
- [ ] Store the app IDs in `EnvironmentConfig.asset`, not hardcoded

---

## Sources

Payment terms and platform docs are primary sources. eCPM figures are
third-party aggregates — treated as directional only.

[^liftoff]: [In-app advertising in 2026: a complete guide for mobile marketers](https://liftoff.ai/blog/in-app-advertising-in-2026-a-complete-guide-for-mobile-marketers/) — Liftoff, 2026.
[^tenjin]: [Ad Monetization Benchmark Report 2026](https://tenjin.com/blog/ad-mon-gaming-2026/) — Tenjin, Q2 2026. Share and platform-split figures. Note: its eCPM tables are published as chart images with no extractable values.
[^segwise]: [The Publisher's Guide to AppLovin: 2026 Monetization](https://segwise.ai/blog/applovin-publisher-monetization-guide) — Segwise, 2026. DAU thresholds and AXON learning period.
[^admobpay]: [Payments and transactions](https://support.google.com/admob/answer/2772140?hl=en) and [Payment thresholds](https://support.google.com/admob/answer/2772208?hl=en) — Google AdMob Help. **Primary source.**
[^applovinpay]: [MAX dashboard — Payments](https://developers.applovin.com/en/max-dashboard/account/payments/) — AppLovin Support Center. **Primary source.**
[^unitypay]: [Revenue and payment](https://unityads.unity3d.com/help/resources/revenue-and-payment) — Unity. **Primary source.**
[^coinis]: [Rewarded Video Ads: How They Work & 2026 eCPMs](https://coinis.com/glossary/rewarded-video) — Coinis, 2026.
[^udonis]: [eCPMs for Rewarded Video, Interstitial & Banner Ads](https://www.blog.udonis.co/mobile-marketing/mobile-apps/ecpms) — Udonis.
[^coinis2]: [In-App Advertising: Formats, Networks & 2026 Guide](https://coinis.com/glossary/in-app-advertising) — Coinis, 2026. Consent/eCPM impact.
[^adlib]: [iOS 14 ATT: Five-Year Retrospective on Ad Measurement (2026)](https://adlibrary.com/posts/ios-14-att) — AdLibrary, 2026.
[^igaming]: [Google tightens rules for sweepstake casino advertising](https://igamingexpert.com/news/business/google-sweepstake-policies-2025/) — iGaming Expert, Oct 2025.
[^bsn]: [Google Sweepstakes Casino Ad Policy: 2026 Market Impact](https://brightsideofnews.com/gambling/google-sweepstakes-casino-ad-policy-impact-2026/) — BSN, 2026.
[^googleads]: [Update to Gambling and Games Policy: Global (August 2026)](https://support.google.com/adspolicy/answer/17258294?hl=en) — Google Advertising Policies Help. **Primary source.**
[^playpolicy]: [Real-Money Gambling, Games, and Contests](https://support.google.com/googleplay/android-developer/answer/9877032) — Google Play Console Help. **Primary source.**

**ironSource shutdown:** [ironSource Ads direct demand sunset FAQ](https://unity.com/products/ironsource-ads-sunset) (Unity, **primary source**);
[Unity to sell off Supersonic label and close its ironSource Ad Network](https://mobilegamer.biz/unity-to-sell-off-supersonic-label-and-close-its-ironsource-ad-network/) (mobilegamer.biz, Mar 2026);
[Unity Software Inc. Form 8-K](https://www.sec.gov/Archives/edgar/data/1810806/000181080626000016/a2026-03x26exhibit991.htm) (SEC, Mar 2026, **primary source**).
