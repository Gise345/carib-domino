import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: false,
    environment: 'node',
    include: ['src/**/*.test.ts', 'test/**/*.test.ts'],
    // The Firestore-rules suites need the emulator, so they run from
    // vitest.rules.config.ts via `npm run test:rules` instead. (test/rules is
    // the GAME rule engine and belongs in this run.)
    exclude: ['**/node_modules/**', 'test/security/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov', 'html'],
      include: ['src/**/*.ts'],
      exclude: ['src/**/*.test.ts', 'src/index.ts'],
    },
  },
});
