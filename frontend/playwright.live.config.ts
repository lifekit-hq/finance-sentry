import {defineConfig, devices} from '@playwright/test';

// Live smoke suite: runs against a REAL deployed stack (no route mocks, real login,
// production database) — strictly read-only specs. The gateway on the VPS is
// loopback-bound, so this is runnable from the VPS itself (deploy runner, devclaw)
// or through an ssh tunnel. Sign-in goes through Logto and returns to the app's public origin, so set
// E2E_LIVE_BASE_URL to that origin (the deploy workflow passes FRONTEND_BASE_URL); the default only
// serves the health check.
const DEFAULT_BASE_URL = 'http://127.0.0.1:8080';

export default defineConfig({
  testDir: './e2e/live',
  globalSetup: './e2e/live/global-setup.ts',
  fullyParallel: false,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  workers: 1,
  reporter: [['list'], ['json', {outputFile: 'playwright-report/live-results.json'}]],
  use: {
    baseURL: process.env['E2E_LIVE_BASE_URL'] ?? DEFAULT_BASE_URL,
    trace: 'on-first-retry',
    launchOptions: {
      args: ['--no-sandbox', '--disable-dev-shm-usage'],
    },
  },

  projects: [
    {
      name: 'chromium',
      use: {...devices['Desktop Chrome']},
    },
  ],
});
