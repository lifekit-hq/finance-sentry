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
const GOOGLE_CLIENT_URL = 'https://accounts.google.com/gsi/client';

// Stand-in for the GIS script: same contract the sign-in button component calls (initialize,
// renderButton, prompt, cancel), and renderButton injects the same button frame the real one does.
const GOOGLE_CLIENT_STUB = `
  window.google = {accounts: {id: {
    initialize() {},
    prompt() {},
    cancel() {},
    renderButton(container) {
      const frame = document.createElement('iframe');
      frame.src = '${GOOGLE_BUTTON_URL}?stub=1';
      container.appendChild(frame);
    },
  }}};
`;

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

  // The browser enforces the CSP on the script and the frame before Playwright's route answers, so
  // stubbing Google's responses keeps this a real CSP check while taking the live accounts.google.com
  // network (flaky in CI) out of it: the GIS script is admitted and runs, and the browser is allowed
  // to load the button frame from Google.
  test('the login page loads the Google sign-in button with no CSP violation', async ({page}) => {
    const violations = await recordCspViolations(page);
    await page.route(`${API}/**`, route => route.fulfill(json({}, UNAUTHORIZED)));
    await page.route(GOOGLE_CLIENT_URL, route =>
      route.fulfill({status: 200, contentType: 'text/javascript', body: GOOGLE_CLIENT_STUB})
    );
    await page.route(`${GOOGLE_BUTTON_URL}**`, route =>
      route.fulfill({
        status: 200,
        contentType: 'text/html',
        body: '<!doctype html><title>stub</title>',
      })
    );
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
