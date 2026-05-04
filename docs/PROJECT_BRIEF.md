# What You Are Building — Project Brief

**Name:** Pose: Caribbean Dominoes (brand mark: **Pose**) — see [`DECISIONS/0002-product-name.md`](./DECISIONS/0002-product-name.md)
**Company:** INVOVIBE TECH LTD
**Type:** Free-to-play global mobile game (iOS + Android)
**Engine:** Unity 6 LTS + C#
**Target launch:** ~5–6 months from kickoff

---

## The product in one paragraph

A polished, multiplayer mobile dominoes game in the spirit of Ludo Club — same level of animation polish, sound design, social features, and tactile satisfaction — but built around dominoes instead of Ludo, with the Caribbean variants (Jamaican Partner, Cuban, Trinidadian, Puerto Rican) as the cultural and marketing centrepiece. Global from day one, with eleven rulesets at launch covering Caribbean, Latin America, North America, and Europe. Players can match against random global opponents, friends via invite link, or AI bots offline. Free to play with optional premium subscription for ad removal, monthly token grants, and cosmetic skins.

## Who it's for

Casual dominoes players globally, with three concentric audience rings:

1. **Caribbean diaspora and home markets** — the primary audience. Jamaican, Cayman, Trinidadian, Puerto Rican, Cuban, Dominican players who currently play in person or on inferior digital products. This is your differentiator — no major mobile dominoes app does Caribbean variants well.
2. **Latin America** — Cuban, Puerto Rican, and Domino Latino variants are widely played across LatAm. Spanish-language UI is critical here.
3. **Global casual market** — Mexican Train, Block, All Fives serve North American and European players. Wider catalogue means deeper matchmaking pool, which keeps the Caribbean experience fast even when local density is low.

## How it makes money

- **Premium subscription** — $4.99/month or $39.99/year. Removes ads, grants 5,000 tokens monthly, unlocks cosmetic tile/board skins, doubles daily bonus. This is the primary revenue driver.
- **Token packs** — one-time IAPs from $1.99 to $49.99 for in-game tokens used as match entry fees. Tokens are explicitly play-money: no real-world value, no withdrawal, no peer transfer. This keeps the product cleanly outside gambling regulation everywhere.
- **Ads** — free users only. Interstitials between casual matches (never during ranked), rewarded videos for daily-bonus boosts, lobby banners. AdMob with LevelPlay mediation.

## What "polish like Ludo Club" actually means

Ludo Club's success isn't the rules — it's the *feel*. Tile placement has weight and physics. Sounds are crisp and timed to animation frames. Wins trigger particle bursts and confetti. Avatars have idle animations and emote reactions. Daily bonuses use slot-machine reveal animations. The lobby has ambient motion. None of this is the rule engine; all of it is the difference between a product that earns $0.50 ARPU and one that earns $5.

The Unity engine choice is mostly about earning access to the polish ecosystem (DOTween, particle systems, skeletal animation, mature audio engines, UI tooling) — not because dominoes is technically demanding.

## Tech stack at a glance

| Layer | Technology | Why |
|---|---|---|
| Game client | Unity 6 LTS + C# | Industry standard for casual mobile games |
| Realtime multiplayer | Photon Fusion 2 | Authoritative multiplayer with host migration; multi-region |
| Auth, persistence, leaderboards | Firebase (Auth + Firestore) | You already know it, scales fine for this |
| Server-side validation | Cloud Functions (TypeScript) | The trust boundary; only thing that writes wallets |
| Subscription + IAP | RevenueCat | Cross-platform receipts and webhooks without writing your own validator |
| Ads | AdMob + LevelPlay mediation | Highest-fill mediation for casual mobile |
| Analytics | Firebase Analytics + Crashlytics | Funnel tracking + crash reporting |
| Push | Firebase Cloud Messaging | Free at any relevant scale |
| Localization | Unity Localization + Firestore string tables | Six launch locales, more added without binary updates |

## What you're explicitly NOT building

- Real-money gambling or wagering — tokens have no real-world value
- A web version (Unity supports WebGL but it's deferred; mobile-first)
- A desktop client
- Live-streamed tournaments with cash prizes
- An NFT or crypto layer
- A social feed
- Free-text chat at launch (canned phrases + emotes only — moderation is a full-time job at scale)

These can come later if the core game succeeds. Scope discipline is what gets you to launch.

## Definition of success

- **Technical:** 60fps on a mid-range Android (e.g. Pixel 5a equivalent), <200ms p95 turn latency in same-region matches, <0.5% crash-free sessions floor.
- **Product:** D1 retention >35%, D7 retention >15%, D30 retention >5% — these are casual-game industry benchmarks. Hitting them confirms the polish is right.
- **Business:** >1% subscription conversion at any scale = financially sustainable. Your existing Firebase/Drift infrastructure makes the unit economics work even at modest scale.

---

*See `ARCHITECTURE.md` for the full system design. See `../CLAUDE.md` for development conventions and how Claude Code should work in this repo. See `../FIRST_SESSION_PROMPT.md` for how to kick off the first Claude Code session.*
