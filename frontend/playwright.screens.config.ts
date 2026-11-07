import {screenBaselineConfig} from '@lifekit-hq/tokens/screen';
import {defineConfig, devices} from '@playwright/test';

// The screen gates (screen check, route scan, pixel baselines) over every routed screen, served
// the way the other e2e is: the built app on e2e/serve.mjs, every request answered by route mocks.
// Kept apart from playwright.config.ts because the baseline fixture owns the screenshot options
// (exact comparison, `updateSnapshots: 'none'`) and the existing visual specs keep theirs.
// E2E_PORT lets concurrent runs (separate worktrees on one box) each take their own port; CI keeps 4202.
const SPA_PORT = Number(process.env['E2E_PORT'] ?? 4202);
// One route, size and scheme may take two minutes in all (the scan settles, freezes, then judges).
const TEST_TIMEOUT_MS = 120_000;

export default defineConfig({
  ...screenBaselineConfig(),
  testDir: './e2e/screens',
  timeout: TEST_TIMEOUT_MS,
  fullyParallel: false,
  forbidOnly: !!process.env['CI'],
  retries: 0,
  workers: 1,
  // A report-only finding lives in the test's annotations, a baseline difference in its images:
  // keep both, passing or not.
  preserveOutput: 'always',
  outputDir: 'e2e/screens/.output',
  reporter: [['list'], ['json', {outputFile: 'playwright-report/screens.json'}]],
  use: {
    baseURL: `http://localhost:${SPA_PORT}`,
    // The app registers a service worker; requests it handles bypass page.route() mocks.
    serviceWorkers: 'block',
    launchOptions: {
      args: ['--no-sandbox', '--disable-dev-shm-usage'],
    },
  },

  webServer: {
    command: 'node e2e/serve.mjs',
    url: `http://localhost:${SPA_PORT}`,
    reuseExistingServer: false,
    env: {PORT: String(SPA_PORT)},
  },

  projects: [
    {
      name: 'chromium',
      use: {...devices['Desktop Chrome']},
    },
  ],
});
