import {expect, type Locator, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const PHONE = {width: 390, height: 844};
const INSET_TOP = 47;
const INSET_BOTTOM = 34;
const TALL_CONTENT_PX = 3000;

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: ['ai.use']},
  expiresAt: '2027-01-01T00:00:00Z',
};

async function mockApis(page: Page): Promise<void> {
  const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/alerts?**`, route =>
    route.fulfill(
      json({items: [], totalCount: 0, unreadCount: 0, page: 1, pageSize: 20, totalPages: 0})
    )
  );
  await page.route(`${API}/events/**`, route => route.fulfill(json({items: []})));
}

async function box(
  locator: Locator
): Promise<{x: number; y: number; width: number; height: number}> {
  const b = await locator.boundingBox();
  if (!b) {
    throw new Error('element has no box');
  }
  return b;
}

// Installed-PWA shape: the status bar and home indicator are drawn under, so the safe-area insets
// are non-zero. Chromium can simulate them through CDP.
async function simulateInsets(page: Page, top: number, bottom: number): Promise<void> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setSafeAreaInsetsOverride', {insets: {top, bottom, left: 0, right: 0}});
}

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`phone overlay shell (${scheme})`, () => {
    test.use({viewport: PHONE, colorScheme: scheme, hasTouch: true});

    test('tab bar is fully visible and content scrolls under both bars', async ({page}) => {
      await mockApis(page);
      await page.goto('/events');
      await simulateInsets(page, INSET_TOP, INSET_BOTTOM);
      await page.reload();

      const tabBar = page.locator('cmn-bottom-tab-bar');
      const topBar = page.locator('cmn-top-bar');
      await expect(tabBar).toBeVisible();

      const bodyScroll = await page.evaluate(() => ({
        scrollHeight: document.documentElement.scrollHeight,
        innerHeight: window.innerHeight,
      }));
      expect(bodyScroll.scrollHeight).toBeLessThanOrEqual(bodyScroll.innerHeight);

      const tabBox = await box(tabBar);
      expect(tabBox.y + tabBox.height).toBeLessThanOrEqual(PHONE.height);
      expect(tabBox.y + tabBox.height).toBeGreaterThan(PHONE.height - INSET_BOTTOM);

      const main = await box(page.locator('cmn-app-layout main'));
      const topBox = await box(topBar);
      expect(main.y).toBeLessThanOrEqual(topBox.y);
      expect(main.y + main.height).toBeGreaterThanOrEqual(tabBox.y + tabBox.height);

      await page.evaluate(px => {
        const scroller = document.querySelector('cmn-app-layout main .overflow-y-auto');
        if (!scroller) {
          throw new Error('scroller missing');
        }
        const filler = document.createElement('div');
        filler.style.height = `${px}px`;
        scroller.appendChild(filler);
        scroller.scrollTop = px / 2;
      }, TALL_CONTENT_PX);
      const scrolled = await page.evaluate(
        () => document.querySelector('cmn-app-layout main .overflow-y-auto')?.scrollTop
      );
      expect(scrolled ?? 0).toBeGreaterThan(0);

      await page.screenshot({path: `test-results/phone-overlay-${scheme}.png`});
    });
  });
}

test.describe('desktop shell', () => {
  test.use({viewport: {width: 1280, height: 800}});

  test('keeps the sidebar and no bottom tab bar', async ({page}) => {
    await mockApis(page);
    await page.goto('/events');
    await expect(page.locator('cmn-sidebar-nav')).toBeVisible();
    await expect(page.locator('cmn-bottom-tab-bar')).toBeHidden();
  });
});
