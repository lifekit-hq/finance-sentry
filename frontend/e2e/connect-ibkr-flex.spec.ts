import {expect, type Page, test} from '@playwright/test';

// Origin-agnostic glob: the production build calls the relative '/api/v1'.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: [
      'connections.manage',
      'ai.use',
      'mcp.connect',
      'mcp.service',
      'ops.admin',
      'users.manage',
    ],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

const EMPTY_WEALTH = {
  totalNetWorth: 0,
  baseCurrency: 'USD',
  categories: [],
  appliedFilters: {category: null, provider: null},
};

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

const PHONE = {width: 390, height: 844};
const PROOF_DIR = process.env['IBKR_PROOF_DIR'];

const PREVIEW = {
  accountId: 'U1234567',
  fromDate: '2025-10-05',
  toDate: '2026-10-04',
  generatedAtUtc: '2026-10-05T06:30:15Z',
  openPositionsCount: 3,
  cashCurrencies: ['EUR', 'USD'],
  tradesCount: 12,
  cashTransactionsCount: 4,
};

async function openIbkrForm(page: Page): Promise<void> {
  await page.goto('/accounts');
  await page
    .getByRole('button', {name: /Connect/})
    .first()
    .click();
  await page.getByText('Brokerage', {exact: true}).click();
}

async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(overflow).toBeLessThanOrEqual(0);
}

test.describe('Connect Interactive Brokers (Flex)', () => {
  test.use({viewport: PHONE});

  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('checks the query, shows the preview, then saves with the trimmed pair', async ({page}) => {
    let connectBody: unknown;
    await page.route(`${API}/brokerage/ibkr/flex/validate`, route => route.fulfill(json(PREVIEW)));
    await page.route(`${API}/brokerage/ibkr/flex/connect`, route => {
      connectBody = route.request().postDataJSON();
      return route.fulfill(json({}, 201));
    });

    await openIbkrForm(page);
    await expect(page.getByText('Advanced: live prices (optional)')).toBeVisible();
    await page.getByRole('textbox', {name: 'Flex Web Service token'}).fill(' 123 456 ');
    await page.getByRole('textbox', {name: 'Flex query ID'}).fill('987654');
    await expectNoHorizontalScroll(page);
    if (PROOF_DIR) {
      await page.screenshot({path: `${PROOF_DIR}/01-form.png`, fullPage: true});
    }

    await page.getByRole('button', {name: /Check connection/}).click();
    await expect(page.getByText('U1234567')).toBeVisible();
    await expectNoHorizontalScroll(page);
    if (PROOF_DIR) {
      await page.screenshot({path: `${PROOF_DIR}/02-preview.png`, fullPage: true});
    }

    await page.getByRole('button', {name: /Save & connect/}).click();
    await expect(page).toHaveURL(/\/accounts\/investments/);
    expect(connectBody).toEqual({token: '123456', queryId: '987654'});
  });

  test('shows a clear message when the token is invalid', async ({page}) => {
    await page.route(`${API}/brokerage/ibkr/flex/validate`, route =>
      route.fulfill(json({error: 'Invalid token', errorCode: 'IBKR_FLEX_INVALID_TOKEN'}, 422))
    );

    await openIbkrForm(page);
    await page.getByRole('textbox', {name: 'Flex Web Service token'}).fill('bad');
    await page.getByRole('textbox', {name: 'Flex query ID'}).fill('987654');
    await page.getByRole('button', {name: /Check connection/}).click();

    await expect(page.getByText(/token/i).first()).toBeVisible();
    await expect(page.getByRole('button', {name: /Save & connect/})).toHaveCount(0);
    if (PROOF_DIR) {
      await page.screenshot({path: `${PROOF_DIR}/03-error.png`, fullPage: true});
    }
  });
});
