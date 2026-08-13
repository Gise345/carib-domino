# Design System — *Pose: Caribbean Dominoes*

**Status:** v1 — derived from [ADR 0014](./DECISIONS/0014-art-direction.md) (Yard Table, painted hardwood)
**Scope:** UI colour, typography, motion. 3D material and mesh specs live in [`ART_PIPELINE.md`](./ART_PIPELINE.md).

Every contrast figure below is computed, not estimated — see §6 for the method.

---

## 1. The direction in one line

An outdoor Caribbean yard table at night, under string lights. Warm, worn, and
physical. The UI sits *in* that world rather than floating above it: dark
green-black surfaces, bone-white type, lamplight and clay accents.

**Value discipline is the whole game here.** A warm, busy scene is the accepted
risk of this direction ([ADR 0014](./DECISIONS/0014-art-direction.md)). Tiles are
the lightest thing on screen and must stay that way — nothing in the UI may
approach bone-white at scale, or the tiles stop reading as the subject.

---

## 2. Colour tokens

### Surfaces

| Token | Hex | Use |
|---|---|---|
| `night` | `#14322E` | Primary background — deep green-black |
| `night-deep` | `#0C201D` | Vignette, modal scrim, depth |
| `ink` | `#101F1C` | Text on light fills. Never a background |
| `board-paint` | `#2A9D8F` | The board's sun-faded painted top |
| `board-raw` | `#C2A178` | Clean pale timber — fracture interiors |
| `board-grain` | `#8A6A45` | Grain lines, crack depth, worn edges |
| `dust` | `#DCC9A8` | Wood dust and grit, particle tint |

### Tiles

| Token | Hex | Use |
|---|---|---|
| `bone` | `#F2EADA` | Tile face, primary text |
| `bone-worn` | `#DCD2BC` | Tile edge shading, worn areas |
| `pip` | `#1A1614` | Pips — worn black, never pure `#000` |

### Accents & semantics

| Token | Hex | On `night` | Use |
|---|---|---|---|
| `lamplight` | `#E8D5A8` | 9.51:1 | Key light, highlights, premium glow |
| `cta` | `#EE7F5F` | 5.14:1 | Primary buttons |
| `brass` | `#CA8A04` | 4.69:1 | Wins, subscription, premium markers |
| `success` | `#7BB661` | 5.71:1 | — |
| `warning` | `#E9A23B` | 6.36:1 | — |
| `danger` | `#F2705A` | 4.75:1 | — |
| `muted` | `#A89F8E` | 5.25:1 | Secondary text |

All accents clear 4.5:1 on `night` — AA for body text at any size.

### Team colours — 2v2 Partner play

| Token | Hex | On `night` | Luminance |
|---|---|---|---|
| `team-a` | `#F6A87C` | 7.09:1 | 0.491 |
| `team-b` | `#189184` | 3.55:1 | 0.221 |

**These two are separated on luminance, not only hue — deliberately.** The first
palette pass had them at 1.07:1 with near-identical luminance: distinguishable
in full colour, nearly identical in greyscale, in bright sunlight, or to a player
with colour vision deficiency. In Partner play, reading team membership at a
glance *is* the mode, so that was a gameplay defect. They now sit 2.2× apart in
luminance and survive a greyscale test.

Three rules follow, and they are not optional:

1. **Colour never carries team membership alone.** Every team-coded element also
   carries a non-colour indicator — a shape marker, an icon, or position.
2. **`team-b` is a fill, not a text background.** It clears AA-large (3:1) but
   not AA (4.5:1). Labels on `team-b` use `bone` at ≥18pt semibold only. Small
   body text never sits on it.
3. **Test in greyscale.** If a screenshot desaturated to greyscale leaves the two
   teams ambiguous, the screen is wrong.

---

## 3. Typography

| Role | Font | Licence |
|---|---|---|
| Display — headings, buttons, scores | **Baloo 2** | OFL |
| Body / UI — labels, body, numerals | **Nunito** | OFL |

Both are OFL, so commercial embedding in a shipped binary is fine.

**Why this pair.** Baloo 2 is rounded and warm enough to sit in the yard-table
world without tipping into a children's-app register, and it carries genuine
weight at heavy cuts — which the slam-centric identity needs. Nunito shares its
rounded terminals, so the pair reads as one voice, and it is proven at small
sizes on mobile. Both cover Latin-1 Supplement and Latin Extended-A, which is
what the launch locales require.

> **Bebas Neue was rejected** despite being the obvious "impact" display face:
> it is all-caps and its diacritic coverage is weak. With Spanish and French
> locales at launch, accented capitals would have broken or fallen back.

**If a non-Latin locale is ever added, this choice must be revisited.** Neither
face covers Cyrillic, Greek or CJK.

### TextMeshPro setup

TMPro cannot use a `.ttf` directly — it needs a **Font Asset**, a Unity asset
holding a rasterised glyph atlas plus metrics, generated via
*Window → TextMeshPro → Font Asset Creator*.

**Generate both fonts as _Dynamic_ font assets.** This matters more than it
looks. A dynamic atlas rasterises glyphs on demand at runtime; a static one bakes
a fixed character set at build time. Localisation string tables are served
remotely from Firestore (see `CLAUDE.md`), so the shipped binary cannot know
every character a translator will use — a static atlas would render
missing-glyph boxes for any character not pre-baked, and the fix would require a
new store build. Dynamic avoids that entirely.

- Set a fallback chain in *Project Settings → TextMeshPro → Fallback Font Assets*.
- Use **material presets** for outline and drop-shadow variants — a material
  preset is a material variant sharing the font's atlas, so it costs no extra
  atlas memory. Do not duplicate font assets to get a different outline.

### Type scale

| Role | Size (pt) | Font | Weight |
|---|---|---|---|
| Display | 48 | Baloo 2 | ExtraBold |
| Title | 32 | Baloo 2 | Bold |
| Heading | 24 | Baloo 2 | SemiBold |
| Body | 17 | Nunito | Regular |
| Label | 15 | Nunito | SemiBold |
| Caption | 13 | Nunito | Regular |

Body sits at 17pt — mobile minimum is 16. Caption at 13pt is for non-essential
text only; nothing a player must read to make a decision goes below 15pt.

### Numerals — a known gap

Neither face exposes tabular (fixed-width) figures reliably through TMPro, so
scoreboard digits will jitter as totals change. Series scores update constantly
([ADR 0013](./DECISIONS/0013-match-series.md)), which makes this visible.

**Mitigation:** lay out score fields with fixed-width character slots rather than
relying on font metrics. Flagged here so it is designed for, not discovered
during scoreboard polish.

---

## 4. Motion

Easings use DOTween's vocabulary so design and code share one language — the same
convention `ART_PIPELINE.md` §5.2 imposes on the artist's timing sheets.

| Token | Duration | Easing | Use |
|---|---|---|---|
| `ui-instant` | 100ms | `linear` | Toggles, immediate state |
| `ui-tap` | 180ms | `easeOutQuad` | Button press feedback |
| `ui-panel` | 260ms | `easeOutCubic` | Panels, sheets, modals |
| `ui-celebrate` | 420ms | `easeOutBack` | Wins, rewards, unlocks |

UI motion stays in the 100–300ms band, with celebration the single exception.

**UI motion must stay quieter than gameplay motion.** §1 of `ART_PIPELINE.md`
rations spectacle so the slam and the shatter land — a UI that animates as
energetically as the board undercuts the payoff those beats are built for.

---

## 5. Still open

- **Tile back treatment** (§3.3) — seen constantly, in every opponent hand and
  the whole shuffle. Needs a pattern or logo plate. Not yet designed.
- **The four regional skins** — palettes for Jamaica, Cuba, Trinidad and Puerto
  Rico. Per ADR 0014 these must read as distinct from the base, which already
  carries Caribbean signal.
- **Reform beat** (§5.6) — `ART_PIPELINE.md` explicitly asks the artist to
  propose how the board returns. Still unanswered.
- **String-light treatment** — practical lights in the scene, or lighting only?
  Affects whether the background has moving elements competing with the board.

---

## 6. Verifying contrast

Ratios use the WCAG 2.1 relative-luminance formula (sRGB → linear, then
`0.2126R + 0.7152G + 0.0722B`), with `(L_hi + 0.05) / (L_lo + 0.05)`.

Thresholds: **4.5:1** AA body text · **3:1** AA large text (≥18pt, or ≥14pt bold)
· **7:1** AAA.

Re-check any token before changing it. The team-colour figures in §2 are a
constraint, not a preference — they were arrived at by fixing a real failure.
