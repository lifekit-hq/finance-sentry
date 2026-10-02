import {expect, type Page, test} from '@playwright/test';

// The e2e server sends the production nginx's security headers (docker/nginx.security-headers.conf,
// see serve.mjs), so these specs prove the shipped CSP lets the built app run: the inline theme
// bootstrap, the lazy bundles, and the Google Identity Services sign-in button.

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

const UNAUTHORIZED = 401;
const GOOGLE_BUTTON_FRAME = 'iframe[src*="accounts.google.com/gsi/button"]';
const GOOGLE_BUTTON_URL = 'https://accounts.google.com/gsi/button';
const GOOGLE_TIMEOUT_MS = 20_000;

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

  // Google keeps the button frame at 0x0 on an origin its client ID does not list (the e2e server's
  // localhost port), CSP or not, so this asserts what the CSP decides: the GIS script loads and runs,
  // and the browser is allowed to load the button frame from Google.
  test('the login page loads the Google sign-in button with no CSP violation', async ({page}) => {
    const violations = await recordCspViolations(page);
    await page.route(`${API}/**`, route => route.fulfill(json({}, UNAUTHORIZED)));
    const buttonFrameLoaded = page.waitForResponse(
      response => response.url().startsWith(GOOGLE_BUTTON_URL),
      {timeout: GOOGLE_TIMEOUT_MS}
    );

    await page.goto('/login');

    // The inline theme bootstrap ran (admitted by its hash).
    await expect(page.locator('html')).toHaveAttribute('data-theme', /^(light|dark)$/);
    await expect(page.locator(GOOGLE_BUTTON_FRAME)).toBeAttached({timeout: GOOGLE_TIMEOUT_MS});
    await buttonFrameLoaded;
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
