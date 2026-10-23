import {expect, test} from '@playwright/test';

const API = '**/api/v1';
const UNAUTHORIZED = 401;

// There is no fake identity provider in the e2e setup, so this starts where the sign-in callback leaves a refused
// person: on /access-denied with no session. The callback's redirect itself is covered by the API's OIDC tests.
test.describe('access denied', () => {
  test.beforeEach(async ({page}) => {
    const unauthorized = (route: {fulfill: (o: {status: number}) => Promise<void>}) =>
      route.fulfill({status: UNAUTHORIZED});
    await page.route(`${API}/auth/me`, unauthorized);
    await page.route(`${API}/auth/refresh`, unauthorized);
  });

  test('tells a refused person to ask the owner and stays put', async ({page}) => {
    const oidcStarts: string[] = [];
    await page.route(`${API}/auth/oidc/start**`, route => {
      oidcStarts.push(route.request().url());
      return route.fulfill({status: 204});
    });

    await page.goto('/access-denied');

    await expect(page.getByRole('heading', {name: "You don't have access yet"})).toBeVisible();
    await expect(page.getByTestId('access-denied-message')).toContainText(
      'Ask the owner for access.'
    );
    await expect(page).toHaveTitle(/No access/);
    // Unlike /login, the page never forwards to the provider on its own: that would loop back here.
    await page.waitForLoadState('networkidle');
    expect(oidcStarts).toEqual([]);
    await expect(page).toHaveURL(/\/access-denied$/);
  });

  test('Try again starts a fresh provider sign-in', async ({page}) => {
    await page.goto('/access-denied');
    const start = page.waitForRequest(request => request.url().includes('/auth/oidc/start'));
    await page.route(`${API}/auth/oidc/start**`, route => route.fulfill({status: 204}));

    // A 204 never commits the navigation the click starts; without noWaitAfter the click waits on it until the
    // test times out (Playwright 1.64).
    await page.getByRole('button', {name: 'Try again'}).click({noWaitAfter: true});

    await start;
  });
});
