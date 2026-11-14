import {expect, type Page, test} from '@playwright/test';

// Origin-agnostic glob: the production build calls the relative '/api/v1'.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: ['mcp.connect'],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

const REDIRECT_URI = 'http://127.0.0.1:38621/callback/';
const STATE = 'abc123';

function json(body: unknown, status = 200) {
  return {status, contentType: 'application/json', body: JSON.stringify(body)};
}

async function mockSignedIn(page: Page): Promise<void> {
  // Registered first so the specific routes below take precedence.
  await page.route(`${API}/**`, route => route.fulfill(json({})));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
}

test.describe('MCP connect', () => {
  test('a signed-in person is sent on to the API authorize step on the same origin', async ({
    page,
  }) => {
    await mockSignedIn(page);
    const authorize = page.waitForRequest(request =>
      request.url().includes('/api/v1/auth/mcp/authorize')
    );
    await page.route(`${API}/auth/mcp/authorize**`, route =>
      route.fulfill({status: 200, contentType: 'text/html', body: '<p>authorized</p>'})
    );

    await page.goto(`/mcp/connect?redirectUri=${encodeURIComponent(REDIRECT_URI)}&state=${STATE}`);

    const url = new URL((await authorize).url());
    expect(url.origin).toBe(new URL(page.url()).origin);
    expect(url.searchParams.get('redirectUri')).toBe(REDIRECT_URI);
    expect(url.searchParams.get('state')).toBe(STATE);
  });
});
