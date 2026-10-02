import {expect, type Locator, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const PHONE = {width: 390, height: 844};
const INSET_TOP = 47;
const INSET_BOTTOM = 34;
const ALERT_COUNT = 30;

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: ['ai.use']},
  expiresAt: '2027-01-01T00:00:00Z',
};

async function mockApis(page: Page): Promise<void> {
  const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  const items = Array.from({length: ALERT_COUNT}, (_, i) => ({
    id: `alert-${i}`,
    type: 'Opportunity',
    severity: 'Info',
    title: `Alert ${i}`,
    message: 'A populated list long enough to overflow the phone viewport.',
    referenceId: null,
    referenceLabel: null,
    isRead: false,
    isResolved: false,
    createdAt: '2026-10-01T09:00:00Z',
    resolvedAt: null,
  }));
  await page.route(`${API}/alerts?**`, route =>
    route.fulfill(
      json({
        items,
        totalCount: ALERT_COUNT,
        unreadCount: ALERT_COUNT,
        page: 1,
        pageSize: ALERT_COUNT,
        totalPages: 1,
      })
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
      await page.goto('/alerts');
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

      const rows = page.locator('fns-alerts [role="button"]');
      await expect(rows).toHaveCount(ALERT_COUNT);

      const nearestScrollerIsMain = await page.evaluate(() => {
        const mainEl = document.querySelector('cmn-app-layout main');
        const row = document.querySelector('fns-alerts [role="button"]');
        for (let el: Element | null = row; el; el = el.parentElement) {
          const overflowY = getComputedStyle(el).overflowY;
          if (overflowY === 'auto' || overflowY === 'scroll') {
            return el === mainEl;
          }
        }
        return false;
      });
      expect(nearestScrollerIsMain).toBe(true);

      const firstAtRest = await box(rows.first());
      expect(firstAtRest.y).toBeGreaterThanOrEqual(topBox.y + topBox.height);

      await page.locator('cmn-app-layout main').evaluate(el => {
        el.scrollTop = el.scrollHeight / 2;
      });
      const rects = await rows.evaluateAll(els =>
        els.map(el => {
          const r = el.getBoundingClientRect();
          return {top: r.top, bottom: r.bottom};
        })
      );
      const underTopBar = rects.some(r => r.top < topBox.y + topBox.height && r.bottom > topBox.y);
      const underTabBar = rects.some(r => r.top < tabBox.y + tabBox.height && r.bottom > tabBox.y);
      expect(underTopBar).toBe(true);
      expect(underTabBar).toBe(true);

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
