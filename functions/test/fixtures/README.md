# Rule-engine parity fixtures

These JSON files are the cross-language contract that keeps the TypeScript
settlement rule engine (`functions/src/rules/`) in lockstep with the canonical
C# engine (`unity/Assets/_Project/Scripts/Core/`). The C# engine is the
reference; these fixtures capture inputs plus the outcome **C# computed**, and
the Vitest suite (`functions/test/rules/*.parity.test.ts`) asserts the TS port
reproduces them exactly. See ADR 0007.

- **`prng-fixtures.json`** — for several seeds: the first N `nextUInt64` values
  and a sequence of `nextInt(bound)` draws. Proves the SplitMix64 PRNG matches
  bit-for-bit (the deal's determinism rests on this).
- **`replay-fixtures.json`** — many `(seed, players, moves)` games across 2/3/4
  players with their expected outcomes, covering every ending: domino, block,
  draw, and resign.

## Regenerating

The fixtures are produced by a committed dotnet tool that compiles the real C#
engine. **Regenerate whenever the C# engine changes** — if the TS port has
drifted, the parity tests will then fail and pinpoint it (this is exactly the
"passing replay-log fixture test" CLAUDE.md requires when touching either
engine):

```bash
dotnet run --project scripts/replay-fixtures/replay-fixtures.csproj -- functions/test/fixtures
```

The generator is deterministic (its move-selection RNG is fixed-seeded), so a
regenerate with an unchanged engine produces byte-identical files.
