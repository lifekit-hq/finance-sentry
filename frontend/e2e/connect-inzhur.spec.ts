import {expect, type Page, type Request, test} from '@playwright/test';

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

// Placeholders only: no real phone number, password or SMS code ever appears in a test.
const PHONE = '+380 00 000 0000';
const PASSWORD = 'placeholder-password';
const CODE = '000000';

const NOT_CONNECTED = {
  status: 'not_connected',
  hasSavedCredentials: false,
  lastSyncAt: null,
  sessionStartedAt: null,
  loginAvailable: true,
};

function json(body: unknown, status = 200) {
  return {status, contentType: 'application/json', body: JSON.stringify(body)};
}

async function mockApis(page: Page, status: object = NOT_CONNECTED): Promise<void> {
  // Registered first so the specific routes below take precedence.
  await page.route(`${API}/**`, route => route.fulfill(json({})));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/wealth/summary**`, route => route.fulfill(json(EMPTY_WEALTH)));
  await page.route(`${API}/brokerage/inzhur/status`, route => route.fulfill(json(status)));
}

async function openInzhurForm(page: Page): Promise<void> {
  await page.goto('/accounts');
  await page
    .getByRole('button', {name: /Connect/})
    .first()
    .click();
  await page.getByText('Brokerage', {exact: true}).click();

  await expect(page.getByText('Choose your broker.')).toBeVisible();
  await expect(page.getByText('Interactive Brokers', {exact: true})).toBeVisible();
  await page.getByText('Inzhur', {exact: true}).click();

  await expect(page.getByRole('button', {name: 'Send code'})).toBeVisible();
}

async function fillCredentials(page: Page): Promise<void> {
  // cmn-input repeats attributes on its host element; fill the native inputs.
  await page.locator('input[type="tel"]').fill(PHONE);
  await page.locator('input[type="password"]').fill(PASSWORD);
}

test.describe('Connect Inzhur', () => {
  test('phone and password send the code, the code finishes the connection', async ({page}) => {
    await mockApis(page);
    let startRequest: Request | undefined;
    let verifyRequest: Request | undefined;
    await page.route(`${API}/brokerage/inzhur/login/start`, route => {
      startRequest = route.request();
      return route.fulfill(
        json({status: 'code_required', codeExpiresAt: '2026-10-07T10:04:00Z', attemptsLeft: null})
      );
    });
    await page.route(`${API}/brokerage/inzhur/login/verify`, route => {
      verifyRequest = route.request();
      return route.fulfill(json({status: 'connected', codeExpiresAt: null, attemptsLeft: null}));
    });

    await openInzhurForm(page);
    await expect(page.getByText('Read-only', {exact: true})).toBeVisible();
    await fillCredentials(page);
    await page.getByRole('button', {name: 'Send code'}).click();

    await expect(page.getByText(/Inzhur sent a code to your phone/)).toBeVisible();
    await page.locator('input[autocomplete="one-time-code"]').fill(CODE);
    await page.getByRole('button', {name: 'Connect', exact: true}).click();

    await expect(page).toHaveURL(/\/accounts\/investments/);
    expect(startRequest?.method()).toBe('POST');
    expect(startRequest?.postDataJSON()).toEqual({phone: PHONE, password: PASSWORD});
    expect(verifyRequest?.method()).toBe('POST');
    expect(verifyRequest?.postDataJSON()).toEqual({code: CODE});
  });

  test('a wrong code stays on the code step with the attempts left', async ({page}) => {
    await mockApis(page);
    await page.route(`${API}/brokerage/inzhur/login/start`, route =>
      route.fulfill(json({status: 'code_required', codeExpiresAt: null, attemptsLeft: null}))
    );
    await page.route(`${API}/brokerage/inzhur/login/verify`, route =>
      route.fulfill(json({status: 'invalid_code', codeExpiresAt: null, attemptsLeft: 2}))
    );

    await openInzhurForm(page);
    await fillCredentials(page);
    await page.getByRole('button', {name: 'Send code'}).click();
    await page.locator('input[autocomplete="one-time-code"]').fill(CODE);
    await page.getByRole('button', {name: 'Connect', exact: true}).click();

    await expect(page.getByText(/didn't match/)).toBeVisible();
    await expect(page.getByRole('button', {name: 'Connect', exact: true})).toBeVisible();
  });

  test('the daily sign-in limit is explained and no code step opens', async ({page}) => {
    await mockApis(page);
    await page.route(`${API}/brokerage/inzhur/login/start`, route =>
      route.fulfill(
        json({error: 'Daily Inzhur sign-in limit reached.', errorCode: 'INZHUR_LOGIN_LIMIT'}, 429)
      )
    );

    await openInzhurForm(page);
    await fillCredentials(page);
    await page.getByRole('button', {name: 'Send code'}).click();

    await expect(page.getByText(/limited to two attempts a day/)).toBeVisible();
    await expect(page.getByText(/Inzhur sent a code to your phone/)).toHaveCount(0);
  });

  test('a reconnect with saved credentials asks only to send the code', async ({page}) => {
    await mockApis(page, {
      status: 'reauth_required',
      hasSavedCredentials: true,
      lastSyncAt: '2026-10-06T07:00:00Z',
      sessionStartedAt: '2026-10-01T09:00:00Z',
      loginAvailable: true,
    });
    let startRequest: Request | undefined;
    await page.route(`${API}/brokerage/inzhur/login/start`, route => {
      startRequest = route.request();
      return route.fulfill(
        json({status: 'code_required', codeExpiresAt: null, attemptsLeft: null})
      );
    });

    await openInzhurForm(page);

    await expect(page.getByText('Inzhur needs you to sign in again')).toBeVisible();
    await expect(page.locator('input[type="password"]')).toHaveCount(0);
    await page.getByRole('button', {name: 'Send code'}).click();

    await expect(page.getByText(/Inzhur sent a code to your phone/)).toBeVisible();
    expect(startRequest?.postDataJSON()).toEqual({phone: null, password: null});
  });

  test('back from the Inzhur form returns to the broker picker', async ({page}) => {
    await mockApis(page);
    await openInzhurForm(page);

    await page.getByRole('button', {name: 'Back'}).click();

    await expect(page.getByText('Choose your broker.')).toBeVisible();
  });
});
