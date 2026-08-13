# ADR 0014 — Art direction: Yard Table, painted hardwood

- **Status:** Accepted
- **Date:** 2026-08-12
- **Deciders:** Giselle Johnson (Founder/CTO, INVOVIBE TECH LTD)
- **Relates to:** `ART_PIPELINE.md` §10 (this ADR answers all six open questions), `PROJECT_BRIEF.md` (audience, monetisation)

## Context

`ART_PIPELINE.md` specified the full art and animation job but deliberately left
art direction unfixed — §10 listed six direction calls, flagging **Q4 (board
material)** as the blocker for all destruction work (§4.3) and VFX (§6). No final
art could begin until these were answered.

Three coherent directions were evaluated: a premium dark "Domino Hall", a warm
photoreal "Yard Table", and a bright stylised "Carnival". Each bundled a tile
material, pip treatment, setting and board material, because those four cannot be
chosen independently — the board material in particular determines what
"breaking" means.

## Decision

**Yard Table, with a painted hardwood board that cracks and breaks.**

An outdoor Caribbean yard table at night under string lights. The direction was
chosen for its cultural signal — the primary audience is the Caribbean diaspora
and home markets, and that authenticity is the product's stated differentiator.

**The board is a plank-built hardwood table top with a sun-faded painted
surface.** It cracks along the grain and breaks into chunks.

### Splinters are a VFX problem, not a geometry problem

§10 as originally written treated "wood" and "splinters" as a single choice
("Wood splinters. Stone cracks and crumbles."). They are separable, and
conflating them was the error.

Modelling splinters as mesh is what would have read as noise across the 40–60
chunks §4.3 requires, and would have been hard to parse at 6 inches. But fine
splinters and grit are already accounted for as **particles** — `T_VFX_Debris`
in §6 exists precisely for this. So the chunks are blocky and plank-aligned, the
splinters live in the flipbook, and nothing is lost.

An alternative painted-concrete slab was considered and rejected. Wood is the
more recognisable object for a yard table, and it does the job better:

- **Interior contrast (§4.3).** Breaking timber exposes clean, pale raw wood
  beneath a weathered painted top. That is a stronger value break than concrete's
  grey aggregate against grey slab, and it makes §4.3's mandatory "rough,
  unfinished" interior material physically motivated rather than an art
  convention.
- **Crack logic (§4.2).** Wood has grain, so cracks have a *reason* to run the
  way they run — long directional splits following the grain, with shorter
  cross-breaks against it. Concrete cracks radially and randomly, which gives the
  four decal variants far less character to work with.
- **A legible escalation ladder.** Hairline cracks in the paint → paint flaking
  as grain cracks open → full break. Each rung is distinct at phone scale.
- **Chunk silhouettes.** Plank construction produces elongated, plank-aligned
  chunks with natural size variety — which is exactly what §4.3 asks for when it
  warns that uniform chunks look procedural.

### The six answers to §10

| # | Question | Answer |
|---|---|---|
| 1 | Tile material | Aged ivory / bone, worn edges |
| 2 | Pip treatment | Recessed, painted black, worn. Classic black — no per-suit colour coding |
| 3 | Board setting | Outdoor yard table at night, string lights |
| 4 | **Board material** | **Painted hardwood — cracks along the grain, breaks into chunks. No splinter geometry** |
| 5 | Camera | Slight 3/4 angle, ~25–35° |
| 6 | Skin roadmap | Base "Classic" + 4 regional: Jamaica, Cuba, Trinidad, Puerto Rico |

**Camera.** The 3/4 angle means the tile's side and the ~0.6mm bevel (§3.1) are
on screen throughout, so that bevel work pays off and the slam reads with real
vertical depth. Cost: less board legibility on long chains, which the camera
framing must manage as layouts grow.

**Pips stay classic black.** Per-suit colour coding was considered for
readability and rejected — it would fight the worn, authentic tile material, and
§3.4's legibility test is a spacing-and-weight problem, not a colour problem.

**Skins.** Base plus four regional themes at launch. Under §3.5's atlas
structure a skin is seven half-faces, a back and an edge, so four regional
themes is roughly a week of texture work — cheap enough to justify shipping the
subscription's cosmetic hook on day one rather than deferring it.

## Consequences

**Positive**
- Phase 2 art is unblocked; the §10 blocker is closed.
- The palette and type system derive from this decision — see `DESIGN_SYSTEM.md`.
- Grain direction gives the four crack decals (§4.2) genuine variety to exploit
  rather than four takes on the same radial pattern.

**Negative / accepted trade-offs**
- **A busy warm background competes with the tiles.** Yard Table is a richer
  scene than an abstract stage. Depth-of-field, a vignette, and disciplined
  value separation between board and tile faces are now mandatory, not optional.
  Style frames must be tested at true size before any modelling.
- **Grain direction must be decided before the fracture step.** Chunks are
  plank-aligned, so the fracture cannot be a generic radial cell-fracture — the
  point distribution has to respect the grain. This constrains §4.3's Blender
  and Maya workflow and is easy to get wrong on the first pass.
- **Regional skins must not restate the base.** The base theme already carries
  Caribbean signal, so the four regional skins need their own distinct identity
  to feel worth having. This is a design risk to watch in Phase 6.
- **Warm night lighting narrows the usable palette.** Cool UI accents look
  foreign against lamplight, which constrained the team colours — see the
  luminance-separation constraint in `DESIGN_SYSTEM.md`.
- **The 3/4 camera costs board legibility** on double-9 and double-12 layouts if
  those rulesets ship. Revisit framing then.

## References

- `docs/ART_PIPELINE.md` — the artist specification this direction feeds.
- `docs/DESIGN_SYSTEM.md` — palette tokens, typography, motion, TMPro setup.
- `docs/PROJECT_BRIEF.md` — audience rings and the skin monetisation model.
