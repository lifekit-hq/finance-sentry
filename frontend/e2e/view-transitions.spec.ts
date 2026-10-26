import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const PHONE = {width: 390, height: 844};
const FADE_SAMPLE_FRAMES = 12;

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: []},
  expiresAt: '2027-01-01T00:00:00Z',
};

const STAMP = '2026-10-05T00:00:00Z';
const TODAY = new Date().toISOString().slice(0, 10);

const TRANSACTION = {
  transactionId: 't1',
  accountId: 'acc-1',
  bankName: 'AIB',
  currency: 'EUR',
  amount: 10,
  amountUsd: 11,
  date: TODAY,
  postedDate: TODAY,
  description: 'Synthetic row',
  transactionType: 'debit',
  merchantCategory: null,
  isPending: false,
  createdAt: STAMP,
  updatedAt: STAMP,
};

const ALERT = {
  id: 'a1',
  type: 'LowBalance',
  severity: 'Info',
  title: 'Low balance on Revolut',
  message: '',
  referenceId: null,
  referenceLabel: null,
  isRead: true,
  isResolved: false,
  createdAt: STAMP,
  resolvedAt: null,
  occurrenceCount: 1,
  lastOccurredAt: STAMP,
};

async function mockApis(page: Page) {
  const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});
  await page.route(`${API}/accounts**`, route => route.fulfill(json([])));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/alerts?**`, route =>
    route.fulfill(
      json({items: [ALERT], totalCount: 1, unreadCount: 0, page: 1, pageSize: 20, totalPages: 1})
    )
  );
  await page.route(`${API}/accounts/transactions**`, route =>
    route.fulfill(json({items: [TRANSACTION], totalCount: 1, hasMore: false}))
  );
  // Count the view transitions the router starts and lets run (not skipped).
  await page.addInitScript(() => {
    const w = window as unknown as {__transitions: number};
    w.__transitions = 0;
    const start = document.startViewTransition?.bind(document);
    if (start) {
      document.startViewTransition = ((...args: Parameters<typeof start>) => {
        w.__transitions++;
        const transition = start(...args);
        const skip = transition.skipTransition.bind(transition);
        transition.skipTransition = () => {
          w.__transitions--;
          skip();
        };
        return transition;
      }) as typeof document.startViewTransition;
    }
  });
}

const transitions = (page: Page) =>
  page.evaluate(() => (window as unknown as {__transitions: number}).__transitions);

test.describe('Router view transitions', () => {
  test.use({viewport: PHONE, hasTouch: true, isMobile: true});

  test.beforeEach(async ({page}) => mockApis(page));

  test('a tab change crossfades; a filter change does not, and the next tap lands', async ({
    page,
  }) => {
    await page.goto('/transactions');
    await expect(page.getByText('Synthetic row')).toBeVisible();

    const before = await transitions(page);
    await page
      .getByRole('navigation', {name: 'Primary'})
      .getByRole('button', {name: 'Alerts'})
      .tap();
    await expect(page.getByText('Low balance on Revolut')).toBeVisible();
    expect(await transitions(page)).toBeGreaterThan(before);

    await page
      .getByRole('navigation', {name: 'Primary'})
      .getByRole('button', {name: 'Transactions'})
      .tap();
    await expect(page.getByText('Synthetic row')).toBeVisible();

    const afterTabs = await transitions(page);
    const applyPeriod = async (id: string) => {
      await page.getByRole('button', {name: 'Filters', exact: true}).tap();
      await page.getByTestId(id).tap();
      await page.getByRole('button', {name: 'Show results'}).tap();
    };
    await applyPeriod('sheet-period-this-month');
    await expect(page).toHaveURL(/from=/);
    await applyPeriod('sheet-period-last-month');
    await expect(page.getByRole('button', {name: 'Remove Period: Last month'})).toBeVisible();
    expect(await transitions(page)).toBe(afterTabs);
  });

  test('revisiting alerts and the ledger paints their rows at full opacity at once', async ({
    page,
  }) => {
    const firstFrames = (text: string) =>
      page.evaluate(
        async ([label, frames]) => {
          const opacities: number[] = [];
          for (let i = 0; i < (frames as number); i++) {
            const el = [...document.querySelectorAll('*')].find(
              e => e.children.length === 0 && e.textContent?.includes(label)
            );
            if (el) {
              let min = 1;
              for (let n: Element | null = el; n; n = n.parentElement) {
                min = Math.min(min, Number(getComputedStyle(n).opacity));
              }
              opacities.push(min);
            }
            await new Promise(r => requestAnimationFrame(r));
          }
          return opacities;
        },
        [text, FADE_SAMPLE_FRAMES] as const
      );

    await page.goto('/alerts');
    await expect(page.getByText('Low balance on Revolut')).toBeVisible();
    await page
      .getByRole('navigation', {name: 'Primary'})
      .getByRole('button', {name: 'Transactions'})
      .tap();
    await expect(page.getByText('Synthetic row')).toBeVisible();

    await page
      .getByRole('navigation', {name: 'Primary'})
      .getByRole('button', {name: 'Alerts'})
      .tap();
    const alertFrames = await firstFrames('Low balance on Revolut');
    expect(alertFrames.length).toBeGreaterThan(0);
    expect(Math.min(...alertFrames)).toBe(1);

    await page
      .getByRole('navigation', {name: 'Primary'})
      .getByRole('button', {name: 'Transactions'})
      .tap();
    const ledgerFrames = await firstFrames('Synthetic row');
    expect(ledgerFrames.length).toBeGreaterThan(0);
    expect(Math.min(...ledgerFrames)).toBe(1);
  });
});
