import {expect, type Page, type Request, test} from '@playwright/test';

import {API, mockApis} from './support/dossier-mocks';

const PHONE = {width: 390, height: 844};
const DESKTOP = {width: 1440, height: 900};
const SEEN_KEY = 'fns.whatsNew.lastSeen';
const REFERENCE = 'FS-R-42';
const SHOTS = process.env['REPORT_SHOTS_DIR'];

const WHATS_NEW = {
  versions: [
    {
      version: '1.14.0',
      date: '2026-10-05',
      notes: [{text: 'You can show only money in or only money out.', owner: false}],
      changes: ['Group transactions by day'],
    },
  ],
};

async function open(page: Page, path: string): Promise<{requests: Request[]}> {
  const requests: Request[] = [];
  await page.addInitScript(key => localStorage.setItem(key, '1.13.0'), SEEN_KEY);
  await page.route('**/whats-new.json*', route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify(WHATS_NEW)})
  );
  await mockApis(page);
  await page.route(`${API}/alerts/unread-count`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify({count: 0})})
  );
  await page.route(`${API}/feedback`, route => {
    requests.push(route.request());
    return route.fulfill({
      status: 202,
      contentType: 'application/json',
      body: JSON.stringify({reference: REFERENCE}),
    });
  });
  await page.goto(path);
  return {requests};
}

async function shot(page: Page, name: string): Promise<void> {
  if (SHOTS) {
    await page.screenshot({path: `${SHOTS}/${name}.png`});
  }
}

async function fillAndSend(page: Page): Promise<void> {
  await page.getByTestId('report-kind-LooksWrong').click();
  await page.getByTestId('report-text').locator('textarea').fill('The chart is cut off');
  await page.getByRole('button', {name: 'Send report'}).click();
}

test.describe('report a problem (phone)', () => {
  test.use({viewport: PHONE, hasTouch: true, isMobile: true});

  test('More opens the sheet and the report carries only the route pattern', async ({page}) => {
    const {requests} = await open(page, '/more');
    await shot(page, 'phone-more');

    await page.getByTestId('more-report-problem').click();
    await expect(page.getByTestId('report-send')).toBeVisible();
    await expect(
      page.getByText("Don't include account numbers, amounts or passwords")
    ).toBeVisible();
    await shot(page, 'phone-sheet-empty');

    await fillAndSend(page);
    await expect.poll(() => requests.length).toBe(1);
    const body = requests[0].postDataJSON() as Record<string, unknown>;
    expect(body).toMatchObject({
      kind: 'LooksWrong',
      text: 'The chart is cut off',
      route: '/more',
      device: 'Phone',
    });
    expect(Object.keys(body).sort()).toEqual(
      ['appVersion', 'browser', 'device', 'kind', 'os', 'route', 'text'].sort()
    );
    await expect(page.getByText(REFERENCE)).toBeVisible();
    await expect(page.getByTestId('report-send')).toHaveCount(0);
  });

  test('a 429 says to try later and keeps the text', async ({page}) => {
    await open(page, '/more');
    await page.route(`${API}/feedback`, route => route.fulfill({status: 429, body: ''}));

    await page.getByTestId('more-report-problem').click();
    await fillAndSend(page);

    await expect(page.getByTestId('report-error')).toContainText('several reports recently');
    await expect(page.getByTestId('report-text').locator('textarea')).toHaveValue(
      'The chart is cut off'
    );
    await shot(page, 'phone-sheet-rate-limited');
  });
});

test.describe('report a problem (desktop)', () => {
  test.use({viewport: DESKTOP});

  test("the sidebar footer opens What's new and carries the unread dot", async ({page}) => {
    await open(page, '/dashboard');
    const footer = page.locator('aside button', {hasText: "What's new"});

    await expect(footer).toContainText(/v\d+\.\d+\.\d+/);
    await expect(footer.locator('.cmn-badge-indicator')).toBeVisible();
    await shot(page, 'desktop-footer-dot');

    await footer.click();
    await expect(page.getByTestId('whats-new-list')).toContainText('only money in');
    await page.keyboard.press('Escape');
    await expect(footer.locator('.cmn-badge-indicator')).toHaveCount(0);
  });

  test('the avatar menu and the palette open the sheet', async ({page}) => {
    await open(page, '/dashboard');

    await page.getByRole('button', {name: 'Account menu'}).click();
    await page.getByRole('menuitem', {name: 'Report a problem'}).click();
    await expect(page.getByTestId('report-send')).toBeVisible();
    await shot(page, 'desktop-sheet');
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('report-send')).toHaveCount(0);

    await page.keyboard.press('Control+k');
    await page.getByRole('dialog').getByText('Report a problem').click();
    await expect(page.getByTestId('report-send')).toBeVisible();
  });
});
