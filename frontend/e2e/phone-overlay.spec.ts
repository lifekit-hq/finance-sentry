import {expect, type Locator, type Page, test} from '@playwright/test';

const API = '**/api/v1';
// iPhone 17 (CSS px) and its Dynamic Island / home-indicator insets.
const PHONE = {width: 402, height: 874};
const INSET_TOP = 62;
const INSET_BOTTOM = 34;
const TABLET = {width: 820, height: 1180};
const DESKTOP = {width: 1440, height: 900};
const ALERT_COUNT = 30;
const SCROLL_PROBE = 500;

// iOS 26 home-screen web app: the page's viewport starts under the status bar while viewport
// units still resolve to the full screen, so anything sized by them overflows by the inset.
const VIEWPORT_UNITS_TALLER_BY_INSET = `
  .h-screen, .h-dvh, .h-svh, .h-lvh { height: calc(100dvh + ${INSET_TOP}px) !important; }
  .min-h-screen, .min-h-dvh, .min-h-svh, .min-h-lvh { min-height: calc(100dvh + ${INSET_TOP}px) !important; }
`;

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

async function openAlerts(page: Page, insets?: {top: number; bottom: number}): Promise<void> {
  await mockApis(page);
  await page.goto('/alerts');
  if (insets) {
    await simulateInsets(page, insets.top, insets.bottom);
    await page.reload();
  }
  await expect(page.locator('fns-alerts [role="button"]')).toHaveCount(ALERT_COUNT);
}

// The shell never scrolls: html/body fit the viewport, a scroll attempt leaves the window at 0,
// and everything that overflows and scrolls lives in the content region (main). Returns the
// scrollers so a caller can pin them down further.
async function expectShellNeverScrolls(page: Page): Promise<string[]> {
  const result = await page.evaluate(probe => {
    const root = document.documentElement;
    const body = document.body;
    window.scrollTo(0, probe);
    const scrollers = Array.from(document.querySelectorAll('*'))
      .filter(el => {
        const overflowY = getComputedStyle(el).overflowY;
        return (
          (overflowY === 'auto' || overflowY === 'scroll') && el.scrollHeight > el.clientHeight
        );
      })
      .map(el => (el.tagName === 'MAIN' ? 'main' : el.closest('main') ? 'in-main' : 'outside'));
    return {
      rootOverflow: root.scrollHeight - root.clientHeight,
      bodyOverflow: body.scrollHeight - body.clientHeight,
      windowScrollY: window.scrollY,
      scrollers,
    };
  }, SCROLL_PROBE);
  expect(result.rootOverflow).toBeLessThanOrEqual(0);
  expect(result.bodyOverflow).toBeLessThanOrEqual(0);
  expect(result.windowScrollY).toBe(0);
  expect(result.scrollers).not.toContain('outside');
  return result.scrollers;
}

// Phone: the page flows in main, so main is the one and only scroller.
async function expectOnlyMainScrolls(page: Page): Promise<void> {
  expect(await expectShellNeverScrolls(page)).toEqual(['main']);
}

// The tab bar and every label sit inside the viewport above the bottom inset, and no label is
// squeezed: its box is as tall as its text.
async function expectTabBarFullyVisible(page: Page): Promise<void> {
  const nav = page.locator('cmn-bottom-tab-bar nav');
  await expect(nav).toBeVisible();
  const navBox = await box(nav);
  expect(navBox.y).toBeGreaterThanOrEqual(0);
  expect(navBox.y + navBox.height).toBeLessThanOrEqual(PHONE.height - INSET_BOTTOM);

  const labels = await nav.locator('button span.font-label').evaluateAll(els =>
    els.map(el => {
      const r = el.getBoundingClientRect();
      return {
        text: el.textContent?.trim(),
        top: r.top,
        bottom: r.bottom,
        clientHeight: el.clientHeight,
        scrollHeight: el.scrollHeight,
      };
    })
  );
  expect(labels.map(label => label.text)).toEqual([
    'Home',
    'Accounts',
    'Transactions',
    'Alerts',
    'More',
  ]);
  for (const label of labels) {
    expect(label.clientHeight).toBe(label.scrollHeight);
    expect(label.top).toBeGreaterThanOrEqual(navBox.y);
    expect(label.bottom).toBeLessThanOrEqual(navBox.y + navBox.height);
  }
}

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`phone overlay shell (${scheme})`, () => {
    test.use({viewport: PHONE, colorScheme: scheme, hasTouch: true, isMobile: true});

    test('tab bar is fully visible and content scrolls under both bars', async ({page}) => {
      await openAlerts(page, {top: INSET_TOP, bottom: INSET_BOTTOM});

      const tabBar = page.locator('cmn-bottom-tab-bar');
      const topBar = page.locator('cmn-top-bar');
      await expectOnlyMainScrolls(page);
      await expectTabBarFullyVisible(page);

      const tabBox = await box(tabBar);
      expect(tabBox.y + tabBox.height).toBeLessThanOrEqual(PHONE.height);
      expect(tabBox.y + tabBox.height).toBeGreaterThan(PHONE.height - INSET_BOTTOM);

      const main = await box(page.locator('cmn-app-layout main'));
      const topBox = await box(topBar);
      expect(main.y).toBeLessThanOrEqual(topBox.y);
      expect(main.y + main.height).toBeGreaterThanOrEqual(tabBox.y + tabBox.height);

      const rows = page.locator('fns-alerts [role="button"]');
      const firstAtRest = await box(rows.first());
      expect(firstAtRest.y).toBeGreaterThanOrEqual(topBox.y + topBox.height);
      await page.screenshot({path: `test-results/phone-overlay-${scheme}.png`});

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
      await expectOnlyMainScrolls(page);

      await page.screenshot({path: `test-results/phone-overlay-${scheme}-scrolled.png`});
    });

    // Fails on @lifekit-hq/ui 0.6.0 (document scrolls by 62px, tab labels squeezed to 6px) and
    // passes on 0.6.1. Chromium emulation of the iOS shape, not WebKit standalone.
    test('shell does not depend on viewport units (iOS 26 web app shape)', async ({page}) => {
      await openAlerts(page, {top: 0, bottom: INSET_BOTTOM});
      await page.addStyleTag({content: VIEWPORT_UNITS_TALLER_BY_INSET});

      await expectOnlyMainScrolls(page);
      await expectTabBarFullyVisible(page);
    });
  });
}

for (const [name, viewport] of [
  ['tablet', TABLET],
  ['desktop', DESKTOP],
] as const) {
  for (const scheme of ['light', 'dark'] as const) {
    test.describe(`${name} shell (${scheme})`, () => {
      test.use({viewport, colorScheme: scheme});

      test('keeps the sidebar, no bottom tab bar, and only the content scrolls', async ({page}) => {
        await openAlerts(page);
        await expect(page.locator('cmn-sidebar-nav')).toBeVisible();
        await expect(page.locator('cmn-bottom-tab-bar nav')).toBeHidden();

        const layout = await box(page.locator('cmn-app-layout > div').first());
        expect(layout.y).toBe(0);
        expect(layout.height).toBe(viewport.height);
        expect((await expectShellNeverScrolls(page)).length).toBeGreaterThan(0);

        await page.screenshot({path: `test-results/${name}-shell-${scheme}.png`});
      });
    });
  }
}
