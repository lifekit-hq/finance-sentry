import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const PHONE = {width: 402, height: 874};
const TABLET = {width: 820, height: 1180};
const DESKTOP = {width: 1440, height: 900};
const INSET_TOP = 62;
const INSET_BOTTOM = 34;
const SCROLL_PROBE = 500;
const UNAUTHORIZED = 401;
const CENTER_TOLERANCE = 1;

// iOS 26 home-screen web app: the page gets less height than the viewport units resolve to, so
// anything sized with them overflows by the inset.
const VIEWPORT_UNITS_TALLER_BY_INSET = `
  .h-screen, .h-dvh, .h-svh, .h-lvh { height: calc(100dvh + ${INSET_TOP}px) !important; }
  .min-h-screen, .min-h-dvh, .min-h-svh, .min-h-lvh { min-height: calc(100dvh + ${INSET_TOP}px) !important; }
`;

const PAGES = [
  {name: 'login', url: '/login', card: 'fns-login'},
  {name: 'accept-invite', url: '/accept-invite?user=u1&token=t1', card: 'fns-accept-invite'},
  {name: 'mcp-connect', url: '/mcp/connect', card: 'fns-mcp-connect'},
] as const;

const VIEWPORTS = [
  {name: 'phone', size: PHONE, mobile: true},
  {name: 'tablet', size: TABLET, mobile: false},
  {name: 'desktop', size: DESKTOP, mobile: false},
] as const;

async function signedOut(page: Page): Promise<void> {
  const unauthorized = (route: {fulfill: (o: {status: number}) => Promise<void>}) =>
    route.fulfill({status: UNAUTHORIZED});
  await page.route(`${API}/auth/me`, unauthorized);
  await page.route(`${API}/auth/refresh`, unauthorized);
}

for (const vp of VIEWPORTS) {
  for (const scheme of ['light', 'dark'] as const) {
    test.describe(`signed-out pages ${vp.name} (${scheme})`, () => {
      test.use({
        viewport: vp.size,
        colorScheme: scheme,
        hasTouch: vp.mobile,
        isMobile: vp.mobile,
      });

      for (const target of PAGES) {
        test(`${target.name} fits the viewport and never scrolls`, async ({page}) => {
          await signedOut(page);
          await page.goto(target.url);
          if (vp.mobile) {
            const cdp = await page.context().newCDPSession(page);
            await cdp.send('Emulation.setSafeAreaInsetsOverride', {
              insets: {top: 0, bottom: INSET_BOTTOM, left: 0, right: 0},
            });
          }
          await page.addStyleTag({content: VIEWPORT_UNITS_TALLER_BY_INSET});
          await expect(page.locator(target.card)).toBeVisible();

          const result = await page.evaluate(probe => {
            const root = document.documentElement;
            window.scrollTo(0, probe);
            return {
              rootOverflow: root.scrollHeight - root.clientHeight,
              bodyOverflow: document.body.scrollHeight - document.body.clientHeight,
              windowScrollY: window.scrollY,
            };
          }, SCROLL_PROBE);
          expect(result.rootOverflow).toBeLessThanOrEqual(0);
          expect(result.bodyOverflow).toBeLessThanOrEqual(0);
          expect(result.windowScrollY).toBe(0);

          // The card stays vertically centred in the viewport.
          const card = await page.locator(`${target.card} > div > div`).first().boundingBox();
          if (!card) {
            throw new Error('card has no box');
          }
          const above = card.y;
          const below = vp.size.height - (card.y + card.height);
          expect(Math.abs(above - below)).toBeLessThanOrEqual(CENTER_TOLERANCE);

          await page.screenshot({
            path: `test-results/signed-out-${target.name}-${vp.name}-${scheme}.png`,
          });
        });
      }
    });
  }
}
