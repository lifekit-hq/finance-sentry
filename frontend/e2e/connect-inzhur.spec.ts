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

// Placeholder only: no real Inzhur cookie ever appears in a test.
const PASTED = 'fake-refresh-token';

const NOT_CONNECTED = {
  status: 'not_connected',
  lastSyncAt: null,
  sessionStartedAt: null,
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

  await expect(page.getByText('Paste Inzhur session')).toBeVisible();
}

async function pasteSession(page: Page, value = PASTED): Promise<void> {
  // cmn-input repeats attributes on its host element; fill the native input.
  await page.locator('input[type="password"]').fill(value);
  await page.getByRole('button', {name: 'Connect', exact: true}).click();
}

test.describe('Connect Inzhur', () => {
  test('a pasted session connects and lands on investments', async ({page}) => {
    await mockApis(page);
    let sessionRequest: Request | undefined;
    await page.route(`${API}/brokerage/inzhur/session`, route => {
      sessionRequest = route.request();
      return route.fulfill(json({status: 'connected'}));
    });

    await openInzhurForm(page);
    await expect(page.getByText('Read-only', {exact: true})).toBeVisible();
    await expect(page.getByText(/without signing out/)).toBeVisible();
    await pasteSession(page, ` ${PASTED} `);

    await expect(page).toHaveURL(/\/accounts\/investments/);
    expect(sessionRequest?.method()).toBe('POST');
    expect(sessionRequest?.postDataJSON()).toEqual({refreshToken: PASTED});
  });

  test('a session Inzhur refuses is explained and the form stays open', async ({page}) => {
    await mockApis(page);
    await page.route(`${API}/brokerage/inzhur/session`, route =>
      route.fulfill(
        json(
          {error: 'Inzhur did not accept that session.', errorCode: 'INZHUR_SESSION_REJECTED'},
          422
        )
      )
    );

    await openInzhurForm(page);
    await pasteSession(page);

    await expect(page.getByText(/didn't accept that session/)).toBeVisible();
    await expect(page.getByRole('button', {name: 'Connect', exact: true})).toBeVisible();
  });

  test('an empty paste is not sent', async ({page}) => {
    await mockApis(page);
    let sent = false;
    await page.route(`${API}/brokerage/inzhur/session`, route => {
      sent = true;
      return route.fulfill(json({status: 'connected'}));
    });

    await openInzhurForm(page);
    await page.getByRole('button', {name: 'Connect', exact: true}).click();

    await expect(page.getByText('Paste Inzhur session')).toBeVisible();
    expect(sent).toBe(false);
  });

  test('an ended session asks for a fresh one', async ({page}) => {
    await mockApis(page, {
      status: 'reauth_required',
      lastSyncAt: '2026-10-06T07:00:00Z',
      sessionStartedAt: '2026-10-01T09:00:00Z',
    });

    await openInzhurForm(page);

    await expect(page.getByText('Inzhur needs a new session')).toBeVisible();
  });

  test('back from the Inzhur form returns to the broker picker', async ({page}) => {
    await mockApis(page);
    await openInzhurForm(page);

    await page.getByRole('button', {name: 'Back'}).click();

    await expect(page.getByText('Choose your broker.')).toBeVisible();
  });
});
