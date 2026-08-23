import { defineConfig } from 'vitest/config';

/**
 * Firestore rules tests. Kept in their own config because they need the
 * emulator: `npm test` must stay runnable with nothing else started, and a suite
 * that fails when a service is missing teaches people to ignore red.
 *
 * Run them with `npm run test:rules`, which starts the emulator around them.
 */
export default defineConfig({
  test: {
    globals: false,
    environment: 'node',
    include: ['test/security/**/*.test.ts'],
    // Each case round-trips to the emulator; the default 5s is tight on a cold
    // rules engine.
    testTimeout: 20_000,
    hookTimeout: 30_000,
    // Rules evaluation is shared state (one emulator, one project), and the
    // suites clear Firestore between cases.
    fileParallelism: false,
  },
});
