import {expect, type Page, test} from '@playwright/test';

import {API, AUTH_RESPONSE} from './dossier-mocks';

// Shared harness for the per-screen pixel baselines (the same pattern as dashboard-visual.spec.ts):
// route-mocked fixtures, a clock pinned to FROZEN_NOW so month keys and relative timestamps are
// stable, animations disabled by the screenshot options, the sidebar's release version masked, and
// light + dark on a desktop and a 390 px phone. Each screen spec calls `describeVisualBaseline`
// with its own mocks and the element that proves its data has rendered.
// The clock is installed (ticking) rather than frozen: Chart.js animates off Date.now, so a frozen
// clock leaves a chart at zero size. Capture advances the fake clock past the animation
// (CHART_SETTLE_MS) so charts are drawn at their full data size.
export const FROZEN_NOW = new Date('2026-09-15T12:30:00Z');
const CHART_SETTLE_MS = 3000;
// The app shell scrolls inside its own container, so a full-page capture only sees the viewport:
// the viewports are tall enough to hold the whole screen.
const DESKTOP_WIDTH = 1280;
const PHONE_WIDTH = 390;
const DEFAULT_DESKTOP_HEIGHT = 1000;
const DEFAULT_PHONE_HEIGHT = 1400;
const SCREENSHOT_OPTIONS = {
  animations: 'disabled',
  // Glyph anti-aliasing differs a little between the machines that render these baselines.
  maxDiffPixelRatio: 0.001,
} as const;

// A flat grey square stands in for every image the app loads from a third party (coin icons, bank
// favicons): the baselines must not depend on the network or on someone else's artwork.
const PLACEHOLDER_IMAGE = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mO89OTJfwAIsAN+3gv9WAAAAABJRU5ErkJggg==',
  'base64'
);
const EXTERNAL_IMAGES = ['https://assets.coincap.io/**', 'https://www.google.com/s2/favicons**'];

export const json = (body: unknown): {status: number; contentType: string; body: string} => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

export const PROFILE = {
  firstName: 'Test',
  lastName: 'User',
  email: 'test@gmail.com',
  baseCurrency: 'USD',
  theme: 'system',
  emailAlerts: true,
  lowBalanceAlerts: true,
  lowBalanceThreshold: 500,
  syncFailureAlerts: true,
  watchlistAnalystAlerts: false,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
  twoFactor: false,
};

export const EMPTY_PAGE = {items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0};

/** Registers a GET fixture; the glob is relative to the API root, `**` matches a query string. */
export async function mockGet(page: Page, path: string, body: unknown): Promise<void> {
  await page.route(`${API}/${path}`, route => route.fulfill(json(body)));
}

// What every shell-wrapped screen fetches around its own data: the signed-in person, the alerts
// bell and its panel, the floating chat's conversation list, the watchlist and holdings the
// sidebar reads.
export async function mockShell(page: Page): Promise<void> {
  await mockGet(page, 'auth/me', AUTH_RESPONSE);
  await mockGet(page, 'auth/refresh', AUTH_RESPONSE);
  await mockGet(page, 'alerts/unread-count', {count: 0});
  await mockGet(page, 'alerts?**', {...EMPTY_PAGE, unreadCount: 0});
  await mockGet(page, 'agent/conversations', []);
  await mockGet(page, 'research/watchlist', []);
  await mockGet(page, 'brokerage/holdings', {
    provider: 'ibkr',
    syncedAt: '2026-09-15T10:00:00Z',
    isStale: false,
    positions: [],
    totalUsdValue: 0,
  });
  await mockGet(page, 'crypto/holdings', {
    provider: 'binance',
    syncedAt: '2026-09-15T10:00:00Z',
    isStale: false,
    holdings: [],
    totalUsdValue: 0,
  });
  await mockGet(page, 'accounts', {accounts: []});
  await mockGet(page, 'profile', PROFILE);
}

export interface VisualScreen {
  /** Baseline file prefix, e.g. `budgets` -> `budgets-desktop.png`. */
  name: string;
  /** Title of the describe block. */
  title: string;
  /** Route to open. */
  path: string;
  /** Registers the screen's own fixtures; runs after `mockShell`, so it may override a shell mock. */
  mocks: (page: Page) => Promise<void>;
  /** Waits until the screen's data has rendered. */
  ready: (page: Page) => Promise<void>;
  desktopHeight?: number;
  phoneHeight?: number;
}

async function openScreen(page: Page, screen: VisualScreen): Promise<void> {
  await page.clock.install({time: FROZEN_NOW});
  for (const pattern of EXTERNAL_IMAGES) {
    await page.route(pattern, route =>
      route.fulfill({status: 200, contentType: 'image/png', body: PLACEHOLDER_IMAGE})
    );
  }
  await mockShell(page);
  await screen.mocks(page);
  await page.goto(screen.path);
  await screen.ready(page);
  await page.evaluate(() => document.fonts.ready);
  await page.clock.runFor(CHART_SETTLE_MS);
}

async function expectBaseline(page: Page, name: string): Promise<void> {
  // The sidebar footer prints the app version next to "What's new", and the version changes on every
  // release. The element's text may be padded with whitespace, which a regex (unlike a string) is
  // matched against verbatim.
  const version = page.getByText(/^\s*v\d+\.\d+\.\d+ · What's new\s*$/);
  // The More page prints it as a list row of its own.
  const moreVersion = page.getByTestId('more-version');
  await expect(page).toHaveScreenshot(name, {...SCREENSHOT_OPTIONS, mask: [version, moreVersion]});
}

export function describeVisualBaseline(screen: VisualScreen): void {
  // Dates render in the viewer's zone; pin it so a baseline does not depend on the machine.
  test.use({timezoneId: 'UTC', locale: 'en-US'});

  const variants = [
    {
      label: 'desktop',
      dark: false,
      width: DESKTOP_WIDTH,
      height: screen.desktopHeight ?? DEFAULT_DESKTOP_HEIGHT,
    },
    {
      label: 'desktop-dark',
      dark: true,
      width: DESKTOP_WIDTH,
      height: screen.desktopHeight ?? DEFAULT_DESKTOP_HEIGHT,
    },
    {
      label: 'phone',
      dark: false,
      width: PHONE_WIDTH,
      height: screen.phoneHeight ?? DEFAULT_PHONE_HEIGHT,
    },
    {
      label: 'phone-dark',
      dark: true,
      width: PHONE_WIDTH,
      height: screen.phoneHeight ?? DEFAULT_PHONE_HEIGHT,
    },
  ];

  test.describe(`${screen.title} visual baseline`, () => {
    for (const variant of variants) {
      test(`${screen.name} ${variant.label.replace('-', ', ')}`, async ({page}) => {
        await page.emulateMedia({colorScheme: variant.dark ? 'dark' : 'light'});
        await page.setViewportSize({width: variant.width, height: variant.height});
        await openScreen(page, screen);
        await expectBaseline(page, `${screen.name}-${variant.label}.png`);
      });
    }
  });
}
