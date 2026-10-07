import {execFileSync} from 'node:child_process';
import {mkdtempSync, readFileSync} from 'node:fs';
import {createServer, type Server} from 'node:https';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

import {expect, type Page, test} from '@playwright/test';

// The Angular service worker passes a cross-origin request it has no cache rule for (every bank
// favicon, stock logo and crypto icon) to the network with fetch() inside the worker. A worker-context
// fetch is governed by the CSP connect-src of the worker script's own response, not by the page's
// img-src, so a logo the document may display is still refused once the worker controls the page. The
// first visit hides it: no worker yet, the page loads the image itself. This spec runs the built app
// with the worker active under the shipped policy (see serve.mjs) and loads a logo from an img-src host.

// A host the policy lists for images (docker/nginx.security-headers.conf). Chromium maps it to the local
// HTTPS stub below, so the spec needs no live third-party host.
const LOGO_HOST = 'assets.coincap.io';
const LOGO_PATH = '/assets/icons/usdc@2x.png';
const STUB_PORT = 4443;
const API = '**/api/v1';
const UNAUTHORIZED = 401;
const WORKER_TIMEOUT_MS = 30_000;

// 1x1 transparent PNG.
const PIXEL = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64'
);

test.use({
  serviceWorkers: 'allow',
  launchOptions: {
    args: [
      '--no-sandbox',
      '--disable-dev-shm-usage',
      // ignoreHTTPSErrors does not reach the worker's own network requests.
      '--ignore-certificate-errors',
      `--host-resolver-rules=MAP ${LOGO_HOST} 127.0.0.1:${STUB_PORT}`,
    ],
  },
});

let logoHost: Server;

test.beforeAll(async () => {
  const dir = mkdtempSync(join(tmpdir(), 'sw-csp-'));
  const key = join(dir, 'key.pem');
  const cert = join(dir, 'cert.pem');
  execFileSync('openssl', [
    ...['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1'],
    ...['-subj', `/CN=${LOGO_HOST}`, '-keyout', key, '-out', cert],
  ]);
  logoHost = createServer({key: readFileSync(key), cert: readFileSync(cert)}, (_req, res) => {
    res.writeHead(200, {'Content-Type': 'image/png', 'Access-Control-Allow-Origin': '*'});
    res.end(PIXEL);
  });
  await new Promise<void>(resolve => logoHost.listen(STUB_PORT, resolve));
});

test.afterAll(async () => {
  await new Promise(resolve => logoHost.close(resolve));
});

/** Loads the logo as the page would render it and reports whether the image arrived. */
function loadLogo(page: Page): Promise<boolean> {
  return page.evaluate(
    ([host, path]) =>
      new Promise<boolean>(resolve => {
        const image = new Image();
        image.onload = () => resolve(true);
        image.onerror = () => resolve(false);
        image.src = `https://${host}${path}?t=${Date.now()}`;
      }),
    [LOGO_HOST, LOGO_PATH]
  );
}

test.describe('Service worker and the CSP', () => {
  test.beforeEach(async ({page}) => {
    await page.route(`${API}/**`, route =>
      route.fulfill({status: UNAUTHORIZED, contentType: 'application/json', body: '{}'})
    );
  });

  // The mask: with no worker the page fetches the image itself and img-src admits it.
  test('a logo from an img-src host loads while no worker controls the page', async ({page}) => {
    await page.goto('/login');
    expect(await page.evaluate(() => navigator.serviceWorker.controller)).toBeNull();

    expect(await loadLogo(page)).toBe(true);
  });

  test('a logo from an img-src host loads once the worker controls the page', async ({
    page,
    context,
  }) => {
    const workerCspErrors: string[] = [];
    context.on('serviceworker', worker =>
      worker.on('console', message => {
        if (message.text().includes('Content Security Policy')) {
          workerCspErrors.push(message.text());
        }
      })
    );

    await page.goto('/login');
    // Register directly: the app waits for stability (up to 30 s) before it registers the worker.
    await page.evaluate(() => navigator.serviceWorker.register('/ngsw-worker.js'));
    await page.evaluate(() => navigator.serviceWorker.ready);
    await page.reload();
    await expect
      .poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null), {
        timeout: WORKER_TIMEOUT_MS,
      })
      .toBe(true);

    expect(await loadLogo(page)).toBe(true);
    expect(workerCspErrors).toEqual([]);
  });
});
