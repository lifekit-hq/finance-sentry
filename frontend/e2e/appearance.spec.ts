import {expect, type Page, test} from '@playwright/test';

import {mockPreferences} from './support/preferences-mocks';

// Settings > Appearance: cmn-appearance-settings (light/dark plus lk-theme-picker from
// @lifekit-hq/elements). A pick goes to ThemeService (the pre-paint cache in localStorage, re-applied
// through the pre-paint script on the next load) and to the account (PATCH /me/preferences, mocked
// here with a server that remembers).

const API = '**/api/v1';
const SEED_KEY = 'cmn-theme-seed';
const APP_ACCENT = '#175a6d';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: ['ai.use']},
  expiresAt: '2027-01-01T00:00:00Z',
};

const PROFILE = {
  firstName: 'Test',
  lastName: 'User',
  email: 'test@gmail.com',
  baseCurrency: 'USD',
  emailAlerts: false,
  lowBalanceAlerts: false,
  lowBalanceThreshold: 100,
  syncFailureAlerts: false,
  watchlistAnalystAlerts: false,
  safeWithdrawalRate: 0.04,
  realAnnualReturn: 0.05,
  twoFactor: false,
};

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

async function openSettings(page: Page): Promise<{patches: unknown[]}> {
  await page.route(`${API}/**`, route => route.fulfill(json({})));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/profile`, route => route.fulfill(json(PROFILE)));
  const {patches} = await mockPreferences(page);
  await page.goto('/settings');
  await expect(page.getByRole('heading', {name: 'Appearance'})).toBeVisible();
  return {patches};
}

const accent = (page: Page) =>
  page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--color-accent-default').trim()
  );
const storedSeed = (page: Page) => page.evaluate(key => localStorage.getItem(key), SEED_KEY);

const DESKTOP = {width: 1280, height: 1100};

test.describe('Settings > Appearance', () => {
  test('keeps the light/dark control beside the colour picker', async ({page}) => {
    await openSettings(page);
    await expect(page.getByRole('radiogroup', {name: 'Theme'})).toBeVisible();
    await expect(page.locator('lk-theme-picker')).toBeVisible();
    await expect(page.getByText('Synced to your account')).toBeVisible();
  });

  test('starts on the app palette (Petrol) with nothing stored', async ({page}) => {
    await openSettings(page);
    expect(await accent(page)).toBe(APP_ACCENT);
    expect(await storedSeed(page)).toBeNull();
    await expect(page.getByLabel('App default')).toBeChecked();
  });

  test('a picked colour applies, survives a reload, and resets to the app palette', async ({
    page,
  }) => {
    const {patches} = await openSettings(page);

    await page.locator('lk-theme-picker').getByText('Plum', {exact: true}).click();
    await expect.poll(() => accent(page)).not.toBe(APP_ACCENT);
    const picked = await accent(page);
    expect(await storedSeed(page)).not.toBeNull();
    // The library debounces the account write; the account is what a reload reads back.
    await expect.poll(() => patches.length).toBe(1);

    await page.reload();
    await expect(page.getByRole('heading', {name: 'Appearance'})).toBeVisible();
    expect(await accent(page)).toBe(picked);
    await expect(page.getByLabel('Plum')).toBeChecked();

    await page.getByRole('button', {name: "Use the app's colour"}).click();
    await expect.poll(() => accent(page)).toBe(APP_ACCENT);
    expect(await storedSeed(page)).toBeNull();
    await expect.poll(() => patches.length).toBe(2);

    await page.reload();
    await expect(page.getByLabel('App default')).toBeChecked();
    expect(await accent(page)).toBe(APP_ACCENT);
  });

  test('the colour pick leaves the layout where it was', async ({page}) => {
    await openSettings(page);
    // The click scrolls the page, so the layout is read relative to the section heading.
    const layout = async () => {
      const heading = await page.getByRole('heading', {name: 'Appearance'}).boundingBox();
      const picker = await page.locator('lk-theme-picker').boundingBox();
      return {
        top: (picker?.y ?? 0) - (heading?.y ?? 0),
        width: picker?.width,
        height: picker?.height,
      };
    };
    const before = await layout();

    await page.locator('lk-theme-picker').getByText('Plum', {exact: true}).click();
    await expect.poll(() => accent(page)).not.toBe(APP_ACCENT);

    expect(await layout()).toEqual(before);
  });

  for (const scheme of ['light', 'dark'] as const) {
    test(`the Appearance section, ${scheme}`, async ({page}) => {
      await page.emulateMedia({colorScheme: scheme});
      await page.setViewportSize(DESKTOP);
      await openSettings(page);
      await page.evaluate(() => document.fonts.ready);
      await expect(page.locator('lk-theme-picker')).toHaveScreenshot(`appearance-${scheme}.png`, {
        animations: 'disabled',
        maxDiffPixelRatio: 0.001,
      });
    });
  }
});
