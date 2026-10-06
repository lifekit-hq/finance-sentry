import {createHash} from 'node:crypto';

import {expect, type Page, test} from '@playwright/test';

// The pre-paint script and ThemeService come from @lifekit-hq (tokens / ui); this spec pins the
// behaviour the app keeps: no flash of the wrong theme on cold load, the stored choice wins, system
// follows the OS live, and the theme-color metas track the active surface. Runs against the
// production build under the shipped CSP, so a stale script hash fails here too.

const API = '**/api/v1';
const STORAGE_KEY = 'cmn-theme';
const MOBILE = {width: 390, height: 844};
const DESKTOP = {width: 1280, height: 800};

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: ['ai.use']},
  expiresAt: '2027-01-01T00:00:00Z',
};

const PROFILE = {
  firstName: 'Test',
  lastName: 'User',
  email: 'test@gmail.com',
  baseCurrency: 'USD',
  theme: 'system',
  emailAlerts: false,
  lowBalanceAlerts: false,
  lowBalanceThreshold: 100,
  syncFailureAlerts: false,
  safeWithdrawalRate: 4,
  realAnnualReturn: 5,
  twoFactor: false,
};

type Scheme = 'light' | 'dark';

function json(body: unknown, status = 200) {
  return {status, contentType: 'application/json', body: JSON.stringify(body)};
}

async function stubGuestApi(page: Page): Promise<void> {
  await page.route(`${API}/**`, route => route.fulfill(json({}, 401)));
  await page.route(`${API}/auth/methods`, route =>
    route.fulfill(json({oidc: false, passwordLogin: true, googleDirect: false}))
  );
}

async function stubSignedInApi(page: Page): Promise<{putBodies: unknown[]}> {
  const putBodies: unknown[] = [];
  await page.route(`${API}/**`, route => route.fulfill(json({})));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/profile`, route => {
    if (route.request().method() === 'PUT') {
      const body = route.request().postDataJSON();
      putBodies.push(body);
      return route.fulfill(json({...PROFILE, ...body}));
    }
    return route.fulfill(json(PROFILE));
  });
  return {putBodies};
}

/** Records every data-theme value the root element ever holds, from before the first script runs. */
async function recordThemes(page: Page): Promise<() => Promise<string[]>> {
  await page.addInitScript(() => {
    const seen: string[] = [];
    (window as unknown as {themes: string[]}).themes = seen;
    new MutationObserver(() => {
      seen.push(document.documentElement.getAttribute('data-theme') ?? '');
    }).observe(document, {attributes: true, attributeFilter: ['data-theme'], subtree: true});
  });
  return () => page.evaluate(() => (window as unknown as {themes: string[]}).themes);
}

async function seedStoredTheme(page: Page, value: string): Promise<void> {
  await page.addInitScript(([key, theme]) => localStorage.setItem(key, theme), [
    STORAGE_KEY,
    value,
  ] as const);
}

const theme = (page: Page) => page.locator('html').getAttribute('data-theme');
const storedTheme = (page: Page) => page.evaluate(key => localStorage.getItem(key), STORAGE_KEY);
const themeColors = (page: Page) =>
  page.evaluate(() =>
    [...document.querySelectorAll('meta[name="theme-color"]')].map(m => m.getAttribute('content'))
  );

test.describe('Theme bootstrap', () => {
  test('the served inline script is the one the CSP pins', async ({page}) => {
    await stubGuestApi(page);
    const response = await page.goto('/login');
    const html = (await response?.text()) ?? '';
    const inline = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
    const themeScript = inline.find(source => source.includes("setAttribute('data-theme'"));
    expect(themeScript).toBeDefined();
    const hash = `'sha256-${createHash('sha256')
      .update(themeScript ?? '')
      .digest('base64')}'`;
    expect(response?.headers()['content-security-policy']).toContain(hash);
  });

  for (const [name, viewport] of [
    ['390px', MOBILE],
    ['desktop', DESKTOP],
  ] as const) {
    test.describe(name, () => {
      test.use({viewport});

      for (const scheme of ['light', 'dark'] as Scheme[]) {
        test(`no stored choice follows the OS (${scheme}) with no flash on cold load`, async ({
          page,
        }) => {
          await page.emulateMedia({colorScheme: scheme});
          const themes = await recordThemes(page);
          await stubGuestApi(page);

          await page.goto('/login');

          await expect.poll(() => theme(page)).toBe(scheme);
          expect(new Set(await themes())).toEqual(new Set([scheme]));
          expect(await storedTheme(page)).toBeNull();
        });

        test(`a stored ${scheme} choice beats the OS preference`, async ({page}) => {
          const opposite: Scheme = scheme === 'dark' ? 'light' : 'dark';
          await page.emulateMedia({colorScheme: opposite});
          await seedStoredTheme(page, scheme);
          const themes = await recordThemes(page);
          await stubGuestApi(page);

          await page.goto('/login');

          await expect.poll(() => theme(page)).toBe(scheme);
          expect(new Set(await themes())).toEqual(new Set([scheme]));
        });
      }
    });
  }

  test('system mode follows the OS live without persisting, and theme-color tracks it', async ({
    page,
  }) => {
    await page.emulateMedia({colorScheme: 'light'});
    await stubGuestApi(page);
    await page.goto('/login');
    await expect.poll(() => theme(page)).toBe('light');
    const lightColors = await themeColors(page);

    await page.emulateMedia({colorScheme: 'dark'});

    await expect.poll(() => theme(page)).toBe('dark');
    expect(await storedTheme(page)).toBeNull();
    const darkColors = await themeColors(page);
    expect(darkColors).not.toEqual(lightColors);
    expect(new Set(darkColors).size).toBe(1);
  });

  test('a stored choice does not follow OS changes', async ({page}) => {
    await page.emulateMedia({colorScheme: 'light'});
    await seedStoredTheme(page, 'light');
    await stubGuestApi(page);
    await page.goto('/login');

    await page.emulateMedia({colorScheme: 'dark'});

    await page.waitForTimeout(250);
    expect(await theme(page)).toBe('light');
  });

  test('the settings theme choice applies live, persists, and system clears it', async ({page}) => {
    await page.emulateMedia({colorScheme: 'light'});
    const {putBodies} = await stubSignedInApi(page);
    await page.goto('/settings');
    const select = page.locator('#s-theme');
    await expect(select).toBeVisible();
    const choose = async (label: string): Promise<void> => {
      await select.getByRole('combobox').selectOption({label});
      await page.getByRole('button', {name: 'Save Profile'}).click();
    };

    await choose('Dark');
    await expect.poll(() => theme(page)).toBe('dark');
    expect(await storedTheme(page)).toBe('dark');
    await page.reload();
    await expect.poll(() => theme(page)).toBe('dark');

    await choose('System default');
    await expect.poll(() => theme(page)).toBe('light');
    expect(await storedTheme(page)).toBeNull();
    expect(putBodies.length).toBe(2);
  });
});
