import {expect, type Page, test} from '@playwright/test';

// Parse compact ($2.9K) or full-precision ($2,900.00) currency strings to a number.
// Both formats are used: dashboard uses compact notation, ledger uses decimal pipe.
function extractAmount(cardText: string): number {
  const match = cardText.match(/\$[\d,.]+[KkMmBb]?/);
  if (!match) {
    throw new Error(`No dollar amount found in: ${cardText}`);
  }
  const cleaned = match[0].replace(/[$,\s]/g, '');
  const upper = cleaned.toUpperCase();
  if (upper.endsWith('K')) {
    return parseFloat(upper.slice(0, -1)) * 1_000;
  }
  if (upper.endsWith('M')) {
    return parseFloat(upper.slice(0, -1)) * 1_000_000;
  }
  return parseFloat(cleaned);
}

// Origin-agnostic glob, NOT the dev apiBaseUrl. `ng build` defaults to the
// production configuration, which file-replaces environment.ts and makes
// apiBaseUrl the relative '/api/v1' — so the built app calls the e2e server's
// own origin, and mocks pinned to http://localhost:5001 never matched: auth
// failed, every page redirected to login, and all specs failed on a missing
// heading. A glob matches whichever origin the build resolves to.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: [
      'connections.manage',
      'ai.use',
      'mcp.connect',
      'mcp.service',
      'ops.admin',
      'users.manage',
    ],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

// Backend uses "yyyy-MM" format (not "yyyy-MM-dd") — see MoneyFlowStatisticsService.
function currentUtcMonthKey(): string {
  const now = new Date();
  return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, '0')}`;
}

function prevUtcMonthKey(): string {
  const now = new Date();
  const prev = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1));
  return `${prev.getUTCFullYear()}-${String(prev.getUTCMonth() + 1).padStart(2, '0')}`;
}

const DASHBOARD_DATA = {
  aggregatedBalance: {USD: 50000},
  totalNetWorthUsd: 50000,
  accountCount: 3,
  accountsByType: {banking: 2, brokerage: 1},
  monthlyFlow: [
    {
      month: prevUtcMonthKey(),
      currency: 'USD',
      inflow: 5000,
      outflow: 3000,
      net: 2000,
      inflowUsd: 5000,
      outflowUsd: 3000,
      netUsd: 2000,
      familySupportOutflowUsd: 0,
    },
    {
      month: currentUtcMonthKey(),
      currency: 'USD',
      inflow: 4800,
      outflow: 2900,
      net: 1900,
      inflowUsd: 4800,
      outflowUsd: 2900,
      netUsd: 1900,
      // $800 of the $2900 outflow is family-support, so "Spent" = 2900 - 800 = 2100.
      familySupportOutflowUsd: 800,
      // $600 was routed to an investment venue. It is NOT part of outflow (investing is not
      // spending), so it is carved out of the surplus: "Kept" = 4800 - 2900 - 600 = 1300.
      investedOutflowUsd: 600,
    },
  ],
  topCategories: [
    {category: 'FOOD_AND_DRINK', totalSpend: 800, percentOfTotal: 30},
    {category: 'TRAVEL', totalSpend: 500, percentOfTotal: 18},
  ],
  lastSyncTimestamp: null,
};

const NET_WORTH_HISTORY = {
  snapshots: [],
  hasHistory: false,
};

const SCRUB_HISTORY = {
  hasHistory: true,
  snapshots: [
    ['2026-09-01', 40_000],
    ['2026-09-15', 45_000],
    ['2026-10-01', 50_000],
  ].map(([snapshotDate, totalNetWorth]) => ({
    snapshotDate,
    bankingTotal: totalNetWorth,
    brokerageTotal: 0,
    cryptoTotal: 0,
    totalNetWorth,
    currency: 'USD',
  })),
};

// Registered after mockApis, so it wins over the empty history there.
async function mockHistory(page: Page): Promise<void> {
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(SCRUB_HISTORY),
    })
  );
}

const INCOME_TRANSACTIONS = {
  items: [
    {
      transactionId: 'tx-1',
      accountId: 'acc-1',
      bankName: 'Test Bank',
      currency: 'USD',
      amount: 2500,
      amountUsd: 2500,
      date: '2026-08-15',
      postedDate: '2026-08-15',
      description: 'Salary August',
      transactionType: 'credit',
      merchantCategory: null,
      isPending: false,
      createdAt: '2026-08-15T00:00:00Z',
      updatedAt: '2026-08-15T00:00:00Z',
    },
  ],
  totalCount: 1,
  hasMore: false,
};

// Transactions for the ledger page — deliberately includes a pending debit and a TRUE
// internal-transfer PAIR (the $1,000 debit leg out of acc-1 and its matching $1,000
// credit leg into acc-2: same amount, same date, mirrored descriptions). The debit leg
// would inflate a client-side sum beyond the backend value (2900); the backend's
// pair detection excludes both legs.
const LEDGER_TRANSACTIONS = {
  items: [
    {
      transactionId: 'tx-1',
      accountId: 'acc-1',
      bankName: 'Test Bank',
      currency: 'USD',
      amount: 400,
      amountUsd: 400,
      date: '2026-08-10',
      postedDate: '2026-08-10',
      description: 'Grocery store',
      transactionType: 'debit',
      merchantCategory: 'FOOD_AND_DRINK',
      isPending: false,
      createdAt: '2026-08-10T00:00:00Z',
      updatedAt: '2026-08-10T00:00:00Z',
    },
    {
      transactionId: 'tx-2',
      accountId: 'acc-1',
      bankName: 'Test Bank',
      currency: 'USD',
      amount: 500,
      amountUsd: 500,
      date: '2026-08-12',
      postedDate: null,
      description: 'Pending payment',
      transactionType: 'debit',
      merchantCategory: null,
      isPending: true,
      createdAt: '2026-08-12T00:00:00Z',
      updatedAt: '2026-08-12T00:00:00Z',
    },
    {
      transactionId: 'tx-3',
      accountId: 'acc-1',
      bankName: 'Test Bank',
      currency: 'USD',
      amount: 1000,
      amountUsd: 1000,
      date: '2026-08-13',
      postedDate: '2026-08-13',
      description: 'Transfer to savings',
      transactionType: 'debit',
      merchantCategory: 'TRANSFER_IN',
      isPending: false,
      createdAt: '2026-08-13T00:00:00Z',
      updatedAt: '2026-08-13T00:00:00Z',
    },
    {
      // The matching credit leg of tx-3's internal transfer — same amount,
      // same date, mirrored description, opposite direction, other account.
      // A category filter alone would miss this pair; description/amount
      // matching is what identifies it.
      transactionId: 'tx-6',
      accountId: 'acc-2',
      bankName: 'Savings Bank',
      currency: 'USD',
      amount: 1000,
      amountUsd: 1000,
      date: '2026-08-13',
      postedDate: '2026-08-13',
      description: 'Transfer from checking',
      transactionType: 'credit',
      merchantCategory: 'TRANSFER_IN',
      isPending: false,
      createdAt: '2026-08-13T00:00:00Z',
      updatedAt: '2026-08-13T00:00:00Z',
    },
    {
      transactionId: 'tx-4',
      accountId: 'acc-2',
      bankName: 'Savings Bank',
      currency: 'USD',
      amount: 2500,
      amountUsd: 2500,
      date: '2026-08-15',
      postedDate: '2026-08-15',
      description: 'Salary August',
      transactionType: 'credit',
      merchantCategory: null,
      isPending: false,
      createdAt: '2026-08-15T00:00:00Z',
      updatedAt: '2026-08-15T00:00:00Z',
    },
  ],
  totalCount: 5,
  hasMore: false,
};

async function mockApis(page: Page): Promise<void> {
  // Silent refresh / auth check on app init
  await page.route(`${API}/auth/me`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
  // Dashboard data — trailing ** because the dashboard scopes the call with ?months=<range>
  await page.route(`${API}/dashboard/aggregated**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(DASHBOARD_DATA),
    })
  );
  // Net-worth history (all ranges)
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(NET_WORTH_HISTORY),
    })
  );
  // Income transactions
  await page.route(`${API}/accounts/transactions**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(INCOME_TRANSACTIONS),
    })
  );
  // Refresh token (called on 401, should not happen but mock it anyway)
  await page.route(`${API}/auth/refresh`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
}

async function mockApisWithLedger(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
  // Like the backend, months=N returns N complete months plus the in-progress one, so even the
  // month-to-date window arrives with the previous month — the dashboard trims it by month key.
  await page.route(`${API}/dashboard/aggregated**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(DASHBOARD_DATA),
    })
  );
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(NET_WORTH_HISTORY),
    })
  );
  await page.route(`${API}/accounts/transactions**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(LEDGER_TRANSACTIONS),
    })
  );
  await page.route(`${API}/auth/refresh`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
}

test.describe('Dashboard drill-downs', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('dashboard renders stat cards for authenticated user', async ({page}) => {
    // The default 3M range must scope the aggregated statistics call.
    const aggregatedRequest = page.waitForRequest(/dashboard\/aggregated\?months=3/);
    await page.goto('/dashboard');
    await aggregatedRequest;
    // Verify the Dashboard heading is visible
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();
    // Stat cards should render (not empty state)
    await expect(page.getByText('Connect your first account')).not.toBeVisible();
    // The in-progress month is totalled in the range tiles, labelled by the selected range.
    await expect(page.getByText('Last 3 months')).toBeVisible();
    await expect(page.getByRole('button', {name: /view income details/i})).toBeVisible();
    await expect(page.getByRole('button', {name: /view spending details/i})).toBeVisible();
    await expect(page.getByRole('button', {name: /savings breakdown/i})).toBeVisible();
  });

  test('month-bucketed charts are labelled as complete months only', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    await expect(page.getByText('Income vs Spending (complete months)')).toBeVisible();
  });

  test('clicking the Income tile navigates to /transactions with credit filter', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // Click the Income drill-down button
    const incomeButton = page.getByRole('button', {name: /view income details/i});
    await expect(incomeButton).toBeVisible();
    await incomeButton.click();

    await expect(page).toHaveURL(/\/transactions.*type=credit/);
  });

  test('clicking the Spending tile navigates to /transactions with debit filter', async ({
    page,
  }) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    const spendingButton = page.getByRole('button', {name: /view spending details/i});
    await expect(spendingButton).toBeVisible();
    await spendingButton.click();

    await expect(page).toHaveURL(/\/transactions.*type=debit/);
  });

  test('the "Breakdown →" link opens the flow breakdown page', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // The four bucket tiles are gone from the dashboard; one link replaces them.
    await expect(page.getByText('Flow breakdown (MTD)')).toHaveCount(0);
    await page.getByRole('link', {name: /breakdown of the selected window/i}).click();

    await expect(page).toHaveURL(/\/dashboard\/breakdown/);
  });
});

const iso = (d: Date): string => d.toISOString().slice(0, 10);

// UTC calendar days, the way the dashboard anchors every window.
function utcDay(offsetDays = 0): Date {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + offsetDays));
}

function monthStart(): Date {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1));
}

test.describe('Dashboard range presets (IBKR set)', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
    await page.route(`${API}/dashboard/flow-breakdown**`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({month: '', items: []}),
      })
    );
  });

  test('offers 1W MTD 1M 3M YTD 1Y ALL, in that order, and no 6M', async ({page}) => {
    await page.goto('/dashboard');
    const bar = page.getByRole('radiogroup', {name: 'History range'});
    await expect(bar.getByRole('radio')).toHaveText(['1W', 'MTD', '1M', '3M', 'YTD', '1Y', 'ALL']);
    await expect(bar.getByRole('radio', {name: '6M', exact: true})).toHaveCount(0);
  });

  test('the range control sits under the chart in the hero card and stays on one row on a phone', async ({
    page,
  }) => {
    await page.setViewportSize({width: 390, height: 844});
    await mockHistory(page);
    await page.goto('/dashboard');
    const bar = page.getByRole('radiogroup', {name: 'History range'});
    const chart = page.locator('cmn-area-chart');
    await expect(bar).toBeVisible();
    await expect(chart).toBeVisible();

    const boxes = await bar.getByRole('radio').evaluateAll(cells =>
      cells.map(c => {
        const {top, height} = c.getBoundingClientRect();
        return {top, height};
      })
    );
    expect(boxes).toHaveLength(7);
    expect(new Set(boxes.map(b => Math.round(b.top))).size).toBe(1);
    expect(Math.min(...boxes.map(b => b.height))).toBeGreaterThanOrEqual(44);

    const chartBottom = await chart.evaluate(el => el.getBoundingClientRect().bottom);
    const barTop = await bar.evaluate(el => el.getBoundingClientRect().top);
    expect(barTop).toBeGreaterThanOrEqual(chartBottom);
  });

  test('scrubbing the chart shows that point in the hero and releasing restores it', async ({
    page,
  }) => {
    await mockHistory(page);
    await page.goto('/dashboard');
    const value = page.getByTestId('net-worth-value');
    await expect(value).toContainText('$50,000.00');
    const canvas = page.locator('cmn-area-chart canvas');
    await expect(canvas).toBeVisible();
    const box = await canvas.boundingBox();
    if (!box) {
      throw new Error('chart canvas has no box');
    }

    await page.mouse.move(box.x + 2, box.y + box.height / 2);
    await expect(value).toContainText('$40,000.00');
    await expect(page.getByTestId('net-worth-change')).toContainText('Sep 1, 2026');

    await page.mouse.move(box.x - 50, box.y - 50);
    await expect(value).toContainText('$50,000.00');
  });

  test('choosing a range updates the selected cell', async ({page}) => {
    await page.goto('/dashboard');
    const bar = page.getByRole('radiogroup', {name: 'History range'});
    await bar.getByRole('radio', {name: '1Y'}).click();
    await expect(bar.getByRole('radio', {name: '1Y'})).toBeChecked();
    await expect(bar.getByRole('radio', {name: '3M'})).not.toBeChecked();
  });

  test('1W asks the API for a day window and the tiles drill into the same seven days', async ({
    page,
  }) => {
    const from = iso(utcDay(-6));
    const to = iso(utcDay());
    await page.goto('/dashboard');
    const aggregated = page.waitForRequest(
      r => r.url().includes('/dashboard/aggregated') && r.url().includes(`windowFrom=${from}`)
    );
    await page
      .getByRole('radiogroup', {name: 'History range'})
      .getByRole('radio', {name: '1W'})
      .click();
    await aggregated;
    await expect(page.getByText('Last 7 days')).toBeVisible();

    await page.getByRole('button', {name: /view spending details/i}).click();
    await expect(page).toHaveURL(
      new RegExp(`/transactions\\?.*type=debit.*from=${from}.*to=${to}`)
    );
  });

  test('MTD starts on the 1st of the UTC month and the Income tile carries it', async ({page}) => {
    const from = iso(monthStart());
    const to = iso(utcDay());
    await page.goto('/dashboard');
    const aggregated = page.waitForRequest(
      r => r.url().includes('/dashboard/aggregated') && r.url().includes(`windowFrom=${from}`)
    );
    await page
      .getByRole('radiogroup', {name: 'History range'})
      .getByRole('radio', {name: 'MTD'})
      .click();
    await aggregated;
    await expect(page.getByText('Month to date')).toBeVisible();

    await page.getByRole('button', {name: /view income details/i}).click();
    await expect(page).toHaveURL(
      new RegExp(`/transactions\\?.*type=credit.*from=${from}.*to=${to}`)
    );
  });

  test('the Savings tile opens the breakdown on the selected window, not the month', async ({
    page,
  }) => {
    const from = iso(utcDay(-6));
    const to = iso(utcDay());
    await page.goto('/dashboard?range=1w');
    await expect(page.getByText('Last 7 days')).toBeVisible();

    const breakdown = page.waitForRequest(
      r =>
        r.url().includes('/dashboard/flow-breakdown') &&
        r.url().includes(`from=${from}`) &&
        r.url().includes(`to=${to}`)
    );
    await page.getByRole('button', {name: /savings breakdown/i}).click();
    await breakdown;

    await expect(page).toHaveURL(/\/dashboard\/breakdown\?from=.*&to=.*&months=1/);
    await expect(page.getByRole('heading', {name: 'Window breakdown'})).toBeVisible();
    await expect(page.getByTestId('breakdown-window')).toBeVisible();
  });

  test('the Breakdown link carries the window too', async ({page}) => {
    const from = iso(monthStart());
    await page.goto('/dashboard?range=mtd');
    await expect(page.getByText('Month to date')).toBeVisible();

    await page.getByRole('link', {name: /breakdown of the selected window/i}).click();

    await expect(page).toHaveURL(new RegExp(`/dashboard/breakdown\\?from=${from}&to=`));
  });

  test('a category row in the table drills into the transactions of the window', async ({page}) => {
    const from = iso(utcDay(-6));
    await page.goto('/dashboard?range=1w');
    await expect(page.getByText('Last 7 days')).toBeVisible();

    await page
      .getByRole('row', {name: /Food And Drink|FOOD_AND_DRINK|Food/i})
      .first()
      .click();

    await expect(page).toHaveURL(new RegExp(`/transactions\\?.*category=.*from=${from}`));
  });
});

test.describe('Retired Income page', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('/income redirects to the credit-filtered ledger so old links still work', async ({
    page,
  }) => {
    await page.goto('/income');

    await expect(page).toHaveURL(/\/transactions.*type=credit/);
    await expect(page.getByText('Salary August')).toBeVisible();
  });
});

test.describe('Transaction ledger — Monthly Outflow stat', () => {
  test.beforeEach(async ({page}) => {
    await mockApisWithLedger(page);
  });

  // The backend mock returns outflowUsd: 2900 for the current month.
  // The page also has a pending debit ($500) and a transfer debit ($1,000) that
  // the backend excludes but a client-side sum would include. This verifies the
  // stat reads the server-side aggregate, not a sum over the loaded page.
  test('Monthly Outflow shows server-side aggregate, not client-side page sum', async ({page}) => {
    await page.goto('/transactions');
    await expect(page.getByRole('heading', {name: 'Transactions', exact: true})).toBeVisible();
    // Backend says $2,900 — the stat must match this, not the $1,900 client-side sum
    // (400 grocery + 500 pending + 1000 transfer = $1,900 from debits in page).
    await expect(page.getByTestId('ledger-summary')).toContainText('$2,900.00 out this month');
  });

  test('Monthly Outflow does not change when Load More is clicked', async ({page}) => {
    // Offset-aware two-page mock (registered last, so it wins over the
    // beforeEach route): page 1 repeats LEDGER_TRANSACTIONS with hasMore,
    // page 2 appends another posted debit ($700) that a client-side sum
    // would fold into the stat. The backend aggregate must not move.
    const pageTwo = {
      items: [
        {
          transactionId: 'tx-5',
          accountId: 'acc-1',
          bankName: 'Test Bank',
          currency: 'USD',
          amount: 700,
          amountUsd: 700,
          date: '2026-08-18',
          postedDate: '2026-08-18',
          description: 'Electronics store',
          transactionType: 'debit',
          merchantCategory: 'GENERAL_MERCHANDISE',
          isPending: false,
          createdAt: '2026-08-18T00:00:00Z',
          updatedAt: '2026-08-18T00:00:00Z',
        },
      ],
      totalCount: 6,
      hasMore: false,
    };
    await page.route(`${API}/accounts/transactions**`, route => {
      const offset = new URL(route.request().url()).searchParams.get('offset');
      const body =
        offset === null || offset === '0'
          ? {...LEDGER_TRANSACTIONS, totalCount: 6, hasMore: true}
          : pageTwo;
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(body),
      });
    });

    await page.goto('/transactions');
    await expect(page.getByRole('heading', {name: 'Transactions', exact: true})).toBeVisible();
    await expect(page.getByTestId('ledger-summary')).toContainText('$2,900.00');

    await page.getByRole('button', {name: /load more/i}).click();
    // The page-2 row rendered ⇒ the append happened…
    await expect(page.getByText('Electronics store')).toBeVisible();
    // …the button is gone (hasMore now false) and the stat did not move.
    await expect(page.getByRole('button', {name: /load more/i})).not.toBeVisible();
    await expect(page.getByTestId('ledger-summary')).toContainText('$2,900.00');
  });
});

// Spans dashboard → ledger in a single navigation so the test directly compares what
// each surface renders from the same mocked API call — not two independent assertions
// against a shared constant. Test data includes a pending debit ($500) and a true
// internal-transfer PAIR (the $1,000 debit leg + its matching credit leg) whose debit
// a client-side sum would include but the backend's pair detection excludes.
// Note: pending transactions ARE included in the backend aggregate (MoneyFlowStatisticsService
// explicitly documents this — a card hold is real spending). The consistency guarantee is
// that BOTH surfaces show the same server-side number, not that pending is excluded.
test.describe('Dashboard → Ledger spending consistency', () => {
  test('the Spending tile and Monthly Outflow show the same underlying number', async ({page}) => {
    await mockApisWithLedger(page);
    // The ledger summary is the current month's outflow, so compare it with month to date.
    await page.goto('/dashboard?range=mtd');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // Read the Spending tile value from the dashboard.
    const spendingCard = page.getByRole('button', {name: /view spending details/i});
    await expect(spendingCard).toBeVisible();
    // The title now comes from the route, so it shows before the tile has loaded its value.
    await expect(spendingCard).toContainText(/\$[\d,.]+/);
    const spendingText = (await spendingCard.innerText()).trim();
    const dashboardAmount = extractAmount(spendingText);

    // Navigate to the drill-down (same button the user clicks in the real flow).
    await page.getByRole('button', {name: /view spending details/i}).click();
    await expect(page).toHaveURL(/\/transactions.*type=debit/);
    await expect(page.getByRole('heading', {name: 'Transactions', exact: true})).toBeVisible();

    // Read the monthly outflow figure from the ledger's summary line.
    const outflowSummary = page.getByTestId('ledger-summary');
    await expect(outflowSummary).toContainText('out this month');
    const ledgerAmount = extractAmount((await outflowSummary.innerText()).trim());

    // Transfer pair (tx-3 debit $1,000 + tx-6 credit $1,000) is in the loaded transaction
    // list but excluded from the backend aggregate. If the ledger used a client-side debit
    // sum it would show $1,900 (400+500+1000 from the page), not $2,900 — the explicit
    // value check below proves the pair is excluded from the ledger, and the equality
    // check proves the dashboard shows the same server-side number.
    expect(ledgerAmount).toBeCloseTo(2900, 1);
    expect(dashboardAmount).toBeCloseTo(ledgerAmount, 1);
  });
});

// Top spendings counts the range's debits per category, including the computed FAMILY_SUPPORT
// bucket and UNCATEGORIZED (null category). A row must open the ledger on exactly that slice:
// the category shown as the ledger's Category chip and the query scoped to debits over the same range.
test.describe('Top spendings → Ledger category drill-down', () => {
  const CATEGORIES = [
    {key: 'FOOD_AND_DRINK', label: 'Food & Drink', sortOrder: 10},
    {key: 'FAMILY_SUPPORT', label: 'Family Support', sortOrder: 135},
    {key: 'UNCATEGORIZED', label: 'Uncategorized', sortOrder: 999},
  ];

  for (const {key, label} of CATEGORIES) {
    test(`the ${label} row opens the debit ledger filtered to ${key}`, async ({page}) => {
      await mockApisWithLedger(page);
      const ledgerQueries: URLSearchParams[] = [];
      await page.route(`${API}/categories`, route =>
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(CATEGORIES),
        })
      );
      await page.route(`${API}/dashboard/aggregated**`, route =>
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            ...DASHBOARD_DATA,
            topCategories: [
              {category: 'FAMILY_SUPPORT', totalSpend: 800, percentOfTotal: 40},
              {category: 'FOOD_AND_DRINK', totalSpend: 700, percentOfTotal: 35},
              {category: 'UNCATEGORIZED', totalSpend: 500, percentOfTotal: 25},
            ],
          }),
        })
      );
      await page.route(`${API}/accounts/transactions**`, route => {
        ledgerQueries.push(new URL(route.request().url()).searchParams);
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(LEDGER_TRANSACTIONS),
        });
      });

      await page.goto('/dashboard?range=mtd');
      await page.locator('cmn-data-table').getByText(label, {exact: true}).click();

      await expect(page).toHaveURL(new RegExp(`/transactions\\?.*category=${key}`));
      expect(new URL(page.url()).searchParams.get('type')).toBe('debit');
      // The ledger's chip formats the key itself (MerchantCategoryUtils), e.g. "Food & drink".
      await expect(page.getByTestId('category-chip')).toContainText(
        new RegExp(`Category: ${label}`, 'i')
      );
      await expect.poll(() => ledgerQueries.at(-1)?.getAll('category')).toEqual([key]);
      const query = ledgerQueries.at(-1);
      expect(query?.get('transactionType')).toBe('debit');
      expect(query?.get('from')).toMatch(/^\d{4}-\d{2}-01$/);
      expect(query?.get('to')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    });
  }
});
