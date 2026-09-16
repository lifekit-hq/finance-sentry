import {expect, type Page, type Request, test} from '@playwright/test';

// Origin-agnostic glob: the production build calls the relative '/api/v1'.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com'},
  expiresAt: '2027-01-01T00:00:00Z',
};

const EMPTY_WEALTH = {
  totalNetWorth: 0,
  baseCurrency: 'USD',
  categories: [],
  appliedFilters: {category: null, provider: null},
};

// Stands in for the PEM a user pastes — the backend, not the SPA, parses it.
const PRIVATE_KEY = '-----BEGIN PRIVATE KEY-----MC4CAQAwBQYDK2VwBCIEIA-----END PRIVATE KEY-----';

function json(body: unknown, status = 200) {
  return {status, contentType: 'application/json', body: JSON.stringify(body)};
}

async function mockApis(page: Page): Promise<void> {
  // Registered first so the specific routes below take precedence.
  await page.route(`${API}/**`, route => route.fulfill(json({})));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/wealth/summary**`, route => route.fulfill(json(EMPTY_WEALTH)));
}

async function openRevolutXForm(page: Page): Promise<void> {
  await page.goto('/accounts');
  await page
    .getByRole('button', {name: /Connect/})
    .first()
    .click();
  await page.getByText('Crypto', {exact: true}).click();

  await expect(page.getByText('Choose your crypto exchange.')).toBeVisible();
  await expect(page.getByText('Binance', {exact: true})).toBeVisible();
  await page.getByText('Revolut X', {exact: true}).click();

  await expect(page.getByRole('button', {name: 'Connect Revolut X'})).toBeVisible();
}

async function fillKeyPair(page: Page): Promise<void> {
  // cmn-input repeats the placeholder on its host element; fill the native input.
  await page.locator('input[placeholder="Revolut X API key"]').fill('  revx-api-key  ');
  await page.locator('input[placeholder="-----BEGIN PRIVATE KEY-----"]').fill(PRIVATE_KEY);
}

test.describe('Connect Revolut X', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('the crypto tile offers both exchanges and posts the key pair to the Revolut X endpoint', async ({
    page,
  }) => {
    let connectRequest: Request | undefined;
    await page.route(`${API}/crypto/revolut-x/connect`, route => {
      connectRequest = route.request();
      return route.fulfill(
        json(
          {
            message: 'Revolut X account connected successfully.',
            holdingsCount: 2,
            syncedAt: '2026-09-16T09:00:00Z',
          },
          201
        )
      );
    });

    await openRevolutXForm(page);
    await fillKeyPair(page);
    await page.getByRole('button', {name: 'Connect Revolut X'}).click();

    await expect(page).toHaveURL(/\/accounts\/investments/);
    expect(connectRequest?.method()).toBe('POST');
    expect(connectRequest?.postDataJSON()).toEqual({
      apiKey: 'revx-api-key',
      privateKey: PRIVATE_KEY,
    });
  });

  test('a rejected key pair stays on the form with guidance', async ({page}) => {
    await page.route(`${API}/crypto/revolut-x/connect`, route =>
      route.fulfill(
        json(
          {error: 'Revolut X API error (HTTP 401): Unauthorized', errorCode: 'INVALID_CREDENTIALS'},
          422
        )
      )
    );

    await openRevolutXForm(page);
    await fillKeyPair(page);
    await page.getByRole('button', {name: 'Connect Revolut X'}).click();

    await expect(page.getByText(/Revolut X rejected the key pair/)).toBeVisible();
    await expect(page.getByRole('button', {name: 'Connect Revolut X'})).toBeVisible();
  });

  test('back from the Revolut X form returns to the exchange picker', async ({page}) => {
    await openRevolutXForm(page);

    await page.getByRole('button', {name: 'Back'}).click();

    await expect(page.getByText('Choose your crypto exchange.')).toBeVisible();
  });
});
