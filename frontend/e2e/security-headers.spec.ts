import {expect, type Page, test} from '@playwright/test';

// The e2e server sends the production nginx's security headers (docker/nginx.security-headers.conf,
// see serve.mjs), so these specs prove the shipped CSP lets the built app run: the inline theme
// bootstrap and the lazy bundles.

// Origin-agnostic glob: the production build calls the relative '/api/v1'.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: ['connections.manage', 'ai.use', 'ops.admin'],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

const SIGN_IN_METHODS = {oidc: false, passwordLogin: true};

const UNAUTHORIZED = 401;

function json(body: unknown, status = 200) {
  return {status, contentType: 'application/json', body: JSON.stringify(body)};
}

/** Records every CSP violation the page reports, from before the first script runs. */
async function recordCspViolations(page: Page): Promise<() => Promise<string[]>> {
  await page.addInitScript(() => {
    const violations: string[] = [];
    (window as unknown as {cspViolations: string[]}).cspViolations = violations;
    document.addEventListener('securitypolicyviolation', event => {
      violations.push(`${event.effectiveDirective} blocked ${event.blockedURI || 'inline'}`);
    });
  });
  return () =>
    page.evaluate(() => (window as unknown as {cspViolations: string[]}).cspViolations ?? []);
}

/** The remote (https) sources a directive lists in a CSP. */
function remoteSources(csp: string, directive: string): string[] {
  const sources = csp
    .split(';')
    .map(part => part.trim().split(/\s+/))
    .find(([name]) => name === directive);
  return (sources ?? []).filter(source => source.startsWith('https://')).sort();
}

test.describe('Security headers', () => {
  test('the document carries the CSP and the hardening headers', async ({page}) => {
    await page.route(`${API}/**`, route => route.fulfill(json({}, UNAUTHORIZED)));

    const response = await page.goto('/login');
    const headers = response?.headers() ?? {};

    expect(headers['content-security-policy']).toContain("frame-ancestors 'none'");
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['x-frame-options']).toBe('DENY');
    expect(headers['referrer-policy']).toBe('strict-origin-when-cross-origin');
    expect(headers['permissions-policy']).toBeTruthy();
  });

  // The Angular service worker fetches cross-origin logos itself; that fetch is checked against the
  // connect-src of the /ngsw-worker.js response, so every host img-src admits must be listed there
  // too (service-worker-csp.spec.ts proves it loads). Anything else in img-src or connect-src is a
  // deliberate, separate decision.
  test('the worker script is served with a connect-src covering every img-src host', async ({
    request,
  }) => {
    const response = await request.get('/ngsw-worker.js');
    const csp = response.headers()['content-security-policy'] ?? '';
    const imgHosts = remoteSources(csp, 'img-src');
    const connectHosts = remoteSources(csp, 'connect-src');

    expect(imgHosts.length).toBeGreaterThan(0);
    expect(connectHosts).toEqual(expect.arrayContaining(imgHosts));
    expect(csp).toContain("connect-src 'self'");
  });

  // The login page runs under the shipped CSP: the inline theme bootstrap is admitted by its hash and
  // nothing the page loads is blocked.
  test('the login page loads with no CSP violation', async ({page}) => {
    const violations = await recordCspViolations(page);
    await page.route(`${API}/**`, route => route.fulfill(json({}, UNAUTHORIZED)));
    // Anonymous endpoint: answering it 401 would make the auth interceptor log out in a loop.
    await page.route(`${API}/auth/methods`, route => route.fulfill(json(SIGN_IN_METHODS)));

    await page.goto('/login');

    await expect(page.locator('html')).toHaveAttribute('data-theme', /^(light|dark)$/);
    await expect(page.getByRole('button', {name: /sign in/i}).first()).toBeVisible();
    expect(await violations()).toEqual([]);
  });

  test('the signed-in shell loads its lazy pages with no CSP violation', async ({page}) => {
    const violations = await recordCspViolations(page);
    await page.route(`${API}/**`, route => route.fulfill(json({})));
    await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));

    await page.goto('/accounts');

    await expect(page.getByRole('button', {name: /Connect/}).first()).toBeVisible();
    expect(await violations()).toEqual([]);
  });
});
