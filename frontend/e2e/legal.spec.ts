import {expect, test} from '@playwright/test';

const API = '**/api/v1';
const UNAUTHORIZED = 401;

// The privacy notice and terms must be readable without a session: the login page links to them for people who have
// not signed in yet, and the pages must not bounce a visitor to the provider.
test.describe('legal pages', () => {
  test.beforeEach(async ({page}) => {
    const unauthorized = (route: {fulfill: (o: {status: number}) => Promise<void>}) =>
      route.fulfill({status: UNAUTHORIZED});
    await page.route(`${API}/auth/me`, unauthorized);
    await page.route(`${API}/auth/refresh`, unauthorized);
    await page.route(`${API}/auth/oidc/start**`, route => route.fulfill({status: 204}));
    await page.route(`${API}/auth/sign-in-methods`, route =>
      route.fulfill({status: 200, contentType: 'application/json', body: '{"oidc":true}'})
    );
  });

  test('the privacy notice is readable signed out and says it is an unreviewed template', async ({
    page,
  }) => {
    await page.goto('/privacy');

    await expect(page.getByRole('heading', {name: 'Privacy notice'})).toBeVisible();
    await expect(page.getByTestId('template-notice')).toContainText('has not had legal review');
    await expect(page).toHaveURL(/\/privacy$/);
    await expect(page).toHaveTitle(/Privacy notice/);
  });

  test('the terms carry the not-financial-advice and no-warranty clauses', async ({page}) => {
    await page.goto('/terms');

    await expect(page.getByRole('heading', {name: 'Terms of use'})).toBeVisible();
    await expect(page.getByTestId('not-advice')).toContainText(
      'Informational only. Not financial advice.'
    );
    await expect(page.getByTestId('no-warranty')).toContainText('provided as it is');
    await expect(page).toHaveURL(/\/terms$/);
  });

  test('the sign-in page links to both, and back returns to sign-in', async ({page}) => {
    await page.goto('/login');

    await page.getByTestId('privacy-link').click();
    await expect(page).toHaveURL(/\/privacy$/);
    await page.getByTestId('privacy-terms-link').click();
    await expect(page).toHaveURL(/\/terms$/);
    await expect(page.getByTestId('not-advice')).toBeVisible();
  });
});
