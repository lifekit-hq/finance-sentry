import {expect, type Page, test} from '@playwright/test';

// Parse compact ($2.9K) or full-precision ($2,900.00) currency strings to a number.
// Both formats are used: dashboard uses compact notation, ledger uses decimal pipe.
function extractAmount(cardText: string): number {
  const match = cardText.match(/\$[\d,.]+[KkMmBb]?/);
  if (!match) throw new Error(`No dollar amount found in: ${cardText}`);
  const cleaned = match[0].replace(/[$,\s]/g, '');
  const upper = cleaned.toUpperCase();
  if (upper.endsWith('K')) return parseFloat(upper.slice(0, -1)) * 1_000;
  if (upper.endsWith('M')) return parseFloat(upper.slice(0, -1)) * 1_000_000;
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
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner']},
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
    // The in-progress month lives on the month-to-date tiles and nowhere else.
    await expect(page.getByText('Income (MTD)')).toBeVisible();
    await expect(page.getByText('Spending (MTD)')).toBeVisible();
    await expect(page.getByText('Savings rate (MTD)')).toBeVisible();
  });

  test('month-bucketed charts are labelled as complete months only', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    await expect(page.getByText('Income vs Spending (complete months)')).toBeVisible();
  });

  test('clicking "Income (MTD)" navigates to /transactions with credit filter', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // Click the Income drill-down button
    const incomeButton = page.getByRole('button', {name: /view income details/i});
    await expect(incomeButton).toBeVisible();
    await incomeButton.click();

    await expect(page).toHaveURL(/\/transactions.*type=credit/);
  });

  test('clicking "Spending (MTD)" navigates to /transactions with debit filter', async ({page}) => {
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    const spendingButton = page.getByRole('button', {name: /view spending details/i});
    await expect(spendingButton).toBeVisible();
    await spendingButton.click();

    await expect(page).toHaveURL(/\/transactions.*type=debit/);
  });

  test('flow breakdown splits the month into spent / supported family / invested / kept', async ({
    page,
  }) => {
    // Mock current month: 4800 in, 2900 out (800 of it family support), 600 invested.
    // Spent = 2900 - 800 = 2100; Kept = 4800 - 2900 - 600 = 1300.
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // Flow breakdown section must be visible.
    await expect(page.getByText('Flow breakdown (MTD)')).toBeVisible();

    // All four bucket tiles must be present.
    const spentCard = page.locator('cmn-stat-card').filter({hasText: 'Spent'});
    const familySupportCard = page.locator('cmn-stat-card').filter({hasText: 'Supported family'});
    const investedCard = page.locator('cmn-stat-card').filter({hasText: 'Invested'});
    const keptCard = page.locator('cmn-stat-card').filter({hasText: 'Kept'});

    await expect(spentCard).toBeVisible();
    await expect(familySupportCard).toBeVisible();
    await expect(investedCard).toBeVisible();
    await expect(keptCard).toBeVisible();

    // Supported family must show the backend-reported net expense ($800), not $0 or merged into Spent.
    const familySupportAmount = extractAmount((await familySupportCard.innerText()).trim());
    expect(familySupportAmount).toBeCloseTo(800, 0);

    // Spent must reflect outflow minus family support ($2100), not the full $2900 outflow.
    const spentAmount = extractAmount((await spentCard.innerText()).trim());
    expect(spentAmount).toBeCloseTo(2100, 0);

    // Invested is reported on its own and must not have been folded into spending.
    const investedAmount = extractAmount((await investedCard.innerText()).trim());
    expect(investedAmount).toBeCloseTo(600, 0);

    // Kept is the surplus that was neither spent nor put to work.
    const keptAmount = extractAmount((await keptCard.innerText()).trim());
    expect(keptAmount).toBeCloseTo(1300, 0);
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
    await expect(page.getByRole('heading', {name: 'Transaction Ledger'})).toBeVisible();
    // Backend says $2,900 — the stat must match this, not the $1,900 client-side sum
    // (400 grocery + 500 pending + 1000 transfer = $1,900 from debits in page).
    await expect(page.getByText('$2,900.00')).toBeVisible();
    await expect(page.getByText('Monthly Outflow')).toBeVisible();
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
    await expect(page.getByRole('heading', {name: 'Transaction Ledger'})).toBeVisible();
    await expect(page.getByText('$2,900.00')).toBeVisible();

    await page.getByRole('button', {name: /load more/i}).click();
    // The page-2 row rendered ⇒ the append happened…
    await expect(page.getByText('Electronics store')).toBeVisible();
    // …the button is gone (hasMore now false) and the stat did not move.
    await expect(page.getByRole('button', {name: /load more/i})).not.toBeVisible();
    await expect(page.getByText('$2,900.00')).toBeVisible();
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
  test('Spending (MTD) and Monthly Outflow show the same underlying number', async ({page}) => {
    await mockApisWithLedger(page);
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible();

    // Read the "Spending (MTD)" stat value from the dashboard card.
    const spendingCard = page.locator('cmn-stat-card').filter({hasText: 'Spending (MTD)'});
    await expect(spendingCard).toBeVisible();
    const spendingText = (await spendingCard.innerText()).trim();
    const dashboardAmount = extractAmount(spendingText);

    // Navigate to the drill-down (same button the user clicks in the real flow).
    await page.getByRole('button', {name: /view spending details/i}).click();
    await expect(page).toHaveURL(/\/transactions.*type=debit/);
    await expect(page.getByRole('heading', {name: 'Transaction Ledger'})).toBeVisible();

    // Read the "Monthly Outflow" stat value from the ledger card.
    const outflowCard = page.locator('cmn-stat-card').filter({hasText: 'Monthly Outflow'});
    await expect(outflowCard).toBeVisible();
    const outflowText = (await outflowCard.innerText()).trim();
    const ledgerAmount = extractAmount(outflowText);

    // Transfer pair (tx-3 debit $1,000 + tx-6 credit $1,000) is in the loaded transaction
    // list but excluded from the backend aggregate. If the ledger used a client-side debit
    // sum it would show $1,900 (400+500+1000 from the page), not $2,900 — the explicit
    // value check below proves the pair is excluded from the ledger, and the equality
    // check proves the dashboard shows the same server-side number.
    expect(ledgerAmount).toBeCloseTo(2900, 1);
    expect(dashboardAmount).toBeCloseTo(ledgerAmount, 1);
  });
});
