import {expect, type Page, test} from '@playwright/test';

import {
  API,
  AUTH_RESPONSE,
  DOSSIER_UNKNOWN,
  EMPTY_LEDGER_READ,
  LEDGER_READ_NARRATIVE,
  mockApis,
} from './support/dossier-mocks';

test.describe('Asset Dossier', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('click holding symbol navigates to dossier page', async ({page}) => {
    await page.goto('/accounts/investments');
    // Wait for positions to load and AAPL to appear
    await expect(page.getByText('AAPL').first()).toBeVisible();

    // The whole row is the click target (cmn-data-table rowsActionable)
    await page.getByText('AAPL').first().click();

    // Should land on the dossier URL
    await expect(page).toHaveURL(/\/assets\/AAPL/);
  });

  test('dossier page renders symbol header', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();
  });

  test('dossier header shows price and the day change beside the symbol', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByTestId('dossier-price')).toHaveText('$189.30');
    await expect(page.getByTestId('dossier-day-change')).toHaveText('+1.23%');
  });

  test('dossier header keeps its slot when there is no quote', async ({page}) => {
    await page.goto('/assets/ZZZZ');
    await expect(page.getByRole('heading', {name: 'ZZZZ', level: 1})).toBeVisible();
    await expect(page.getByTestId('dossier-price')).toHaveCount(0);
    const slot = await page.getByTestId('dossier-quote').boundingBox();
    expect(slot?.height).toBeGreaterThanOrEqual(48);
  });

  test('dossier header never quotes a crypto holding by its bare ticker', async ({page}) => {
    const btcDossier = {
      ...DOSSIER_UNKNOWN,
      symbol: 'BTC',
      position: {
        provider: 'binance',
        assetClass: 'Crypto',
        quantity: 0.5,
        currentValueUsd: 30000,
        costBasisUsd: null,
        unrealizedPnlUsd: null,
        unrealizedPnlPercent: null,
        taxLots: [],
      },
    };
    await page.route(`${API}/research/assets/BTC/dossier`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(btcDossier),
      })
    );
    const quoteRequests: string[] = [];
    page.on('request', req => {
      if (req.url().includes('/research/quotes')) {
        quoteRequests.push(req.url());
      }
    });

    await page.goto('/assets/BTC');
    await expect(page.getByRole('heading', {name: 'BTC', level: 1})).toBeVisible();
    await expect(page.getByTestId('dossier-quote')).toBeVisible();
    await expect(page.getByTestId('dossier-price')).toHaveCount(0);
    expect(quoteRequests).toEqual([]);
  });

  test('dossier page renders position section', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Position')).toBeVisible();
    await expect(page.getByText('Current Value')).toBeVisible();
    // Unrealized P&L section
    await expect(page.getByText('Unrealized P&L')).toBeVisible();
  });

  test('dossier page renders thesis section', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Investment Thesis')).toBeVisible();
    await expect(page.getByText('Apple continues to expand its services revenue')).toBeVisible();
    await expect(page.getByText('Active', {exact: true})).toBeVisible();
  });

  test('dossier page renders analyst coverage', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Analyst Coverage')).toBeVisible();
    await expect(page.getByText('Goldman Sachs')).toBeVisible();
  });

  test('dossier page renders recent news', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Recent News')).toBeVisible();
    await expect(page.getByText('Apple reports record services revenue in Q3')).toBeVisible();
  });

  test('the top bar back chevron returns to investments', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();

    await page.getByRole('button', {name: 'Back'}).click();

    await expect(page).toHaveURL(/\/accounts\/investments/);
  });

  test('accounts list brokerage row navigates to dossier on click', async ({page}) => {
    await page.goto('/accounts/list');
    await expect(page.getByText('AAPL').first()).toBeVisible();

    await page.getByText('AAPL').first().click();

    await expect(page).toHaveURL(/\/assets\/AAPL/);
  });

  test('dossier page keeps tax lots collapsed and shows triggers as sentences', async ({page}) => {
    await page.goto('/assets/AAPL');
    const lots = page.getByTestId('tax-lots');
    await expect(lots).toBeVisible();
    await expect(lots).not.toHaveAttribute('open', '');
    await expect(lots.locator('summary')).toContainText('Tax lots (');
    await expect(page.getByText('Revenue growth (YoY) falls below 5.0%')).toBeVisible();
  });

  test('dossier page renders recommendation trend list', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Recommendation Trend')).toBeVisible();
    const trends = page.getByTestId('trend-list');
    await expect(trends.getByTestId('list-row').first()).toContainText('Strong buy');
    // Verify a trend row is rendered
    await expect(trends.getByTestId('list-row').first()).toContainText('18');
  });

  test('dossier page renders the radar trend sparkline for multiple signals', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Radar Signals')).toBeVisible();
    // The sparkline is rendered when there are >= 2 signals
    await expect(page.locator('cmn-line-chart canvas')).toBeVisible();
    // Latest reading is shown in the header
    await expect(page.getByText('Latest')).toBeVisible();
  });

  test('a symbol with no data on file renders a no-data state, not empty sections', async ({
    page,
  }) => {
    await page.goto('/assets/ZZZZ');

    await expect(page.getByRole('heading', {name: 'ZZZZ', level: 1})).toBeVisible();
    await expect(page.getByTestId('dossier-no-data')).toContainText('Nothing on file for ZZZZ');
    // No section card is rendered at all — not even a hollow one.
    await expect(page.getByText('Position', {exact: true})).toHaveCount(0);
    await expect(page.getByText('Investment Thesis')).toHaveCount(0);
    await expect(page.getByText('Radar Signals')).toHaveCount(0);
    await expect(page.getByTestId('ledger-read-card')).toHaveCount(0);
  });
});

test.describe("Ledger's read", () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('offers to generate when nothing is cached', async ({page}) => {
    await page.goto('/assets/AAPL');

    // Nothing generated: a single line with the action, not a card.
    await expect(page.getByTestId('ledger-read-card')).toHaveCount(0);
    await expect(page.getByTestId('ledger-read-empty')).toBeVisible();
    await expect(page.getByTestId('ledger-read-generate')).toBeVisible();
    await expect(page.getByTestId('ledger-read-narrative')).toHaveCount(0);
    // The API reports a missing cache as stale — nothing was ever generated, so no flag.
    await expect(page.getByTestId('ledger-read-stale')).toHaveCount(0);
  });

  test('generate button posts to the agent and renders the returned read', async ({page}) => {
    let postCount = 0;
    await page.route(`${API}/research/assets/AAPL/narrative**`, route => {
      if (route.request().method() === 'POST') {
        postCount += 1;
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            symbol: 'AAPL',
            narrative: LEDGER_READ_NARRATIVE,
            generatedAt: '2026-09-03T09:00:00Z',
            isStale: false,
            cached: false,
          }),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(EMPTY_LEDGER_READ),
      });
    });

    await page.goto('/assets/AAPL');
    await page.getByTestId('ledger-read-generate').click();

    await expect(page.getByTestId('ledger-read-narrative')).toContainText(LEDGER_READ_NARRATIVE);
    await expect(page.getByTestId('ledger-read-regenerate')).toBeVisible();
    expect(postCount).toBe(1);
  });

  test('a cached read renders on load without regenerating', async ({page}) => {
    let postCount = 0;
    await page.route(`${API}/research/assets/AAPL/narrative**`, route => {
      if (route.request().method() === 'POST') {
        postCount += 1;
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          symbol: 'AAPL',
          narrative: LEDGER_READ_NARRATIVE,
          generatedAt: '2026-09-03T09:00:00Z',
          isStale: false,
          cached: true,
        }),
      });
    });

    await page.goto('/assets/AAPL');

    await expect(page.getByTestId('ledger-read-narrative')).toContainText(LEDGER_READ_NARRATIVE);
    await expect(page.getByTestId('ledger-read-generated')).toBeVisible();
    await expect(page.getByTestId('ledger-read-stale')).toHaveCount(0);
    expect(postCount).toBe(0);
  });

  test('a stale cached read still renders, flagged out of date', async ({page}) => {
    await page.route(`${API}/research/assets/AAPL/narrative**`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          symbol: 'AAPL',
          narrative: LEDGER_READ_NARRATIVE,
          generatedAt: '2026-08-01T09:00:00Z',
          isStale: true,
          cached: true,
        }),
      })
    );

    await page.goto('/assets/AAPL');

    await expect(page.getByTestId('ledger-read-narrative')).toContainText(LEDGER_READ_NARRATIVE);
    await expect(page.getByTestId('ledger-read-stale')).toBeVisible();
    await expect(page.getByTestId('ledger-read-regenerate')).toBeVisible();
  });

  test('surfaces a friendly message when the agent is unavailable', async ({page}) => {
    await page.route(`${API}/research/assets/AAPL/narrative**`, route => {
      if (route.request().method() === 'POST') {
        return route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({
            error: 'Ledger could not produce a read right now. Try again shortly.',
            errorCode: 'LEDGER_READ_UNAVAILABLE',
          }),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(EMPTY_LEDGER_READ),
      });
    });

    await page.goto('/assets/AAPL');
    await page.getByTestId('ledger-read-generate').click();

    await expect(page.getByTestId('ledger-read-error')).toContainText(
      'Ledger could not produce a read right now.'
    );
  });
});

test.describe('AI surfaces gated by the ai.use permission', () => {
  const NON_OWNER_AUTH = {
    ...AUTH_RESPONSE,
    user: {...AUTH_RESPONSE.user, roles: ['Member'], permissions: ['connections.manage']},
  };

  async function signInAsMember(page: Page): Promise<void> {
    for (const endpoint of ['auth/me', 'auth/refresh']) {
      await page.route(`${API}/${endpoint}`, route =>
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(NON_OWNER_AUTH),
        })
      );
    }
  }

  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('a Member without ai.use sees no Ledger read block and never requests it', async ({
    page,
  }) => {
    await signInAsMember(page);
    const narrativeRequests: string[] = [];
    page.on('request', req => {
      if (req.url().includes('/narrative')) {
        narrativeRequests.push(req.method());
      }
    });

    await page.goto('/assets/AAPL');

    await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();
    await expect(page.getByTestId('ledger-read-unavailable')).toHaveCount(0);
    await expect(page.getByTestId('ledger-read-empty')).toHaveCount(0);
    await expect(page.getByTestId('ledger-read-card')).toHaveCount(0);
    await expect(page.getByTestId('ledger-read-generate')).toHaveCount(0);
    expect(narrativeRequests).toEqual([]);
  });

  test('a Member without ai.use gets no chat widget, no Ledger nav item, and /ledger redirects', async ({
    page,
  }) => {
    await signInAsMember(page);

    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();
    await expect(page.locator('fns-chat-widget')).toHaveCount(0);
    await expect(
      page.getByRole('navigation').getByRole('button', {name: 'Ledger', exact: true})
    ).toHaveCount(0);

    await page.goto('/ledger');
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('the owner keeps the chat widget, the Ledger nav item and the generate button', async ({
    page,
  }) => {
    await page.goto('/assets/AAPL');

    await expect(page.getByTestId('ledger-read-generate')).toBeVisible();
    await expect(page.getByTestId('ledger-read-unavailable')).toHaveCount(0);
    await expect(page.locator('fns-chat-widget')).toHaveCount(1);
    await expect(
      page.getByRole('navigation').getByRole('button', {name: 'Ledger', exact: true})
    ).toHaveCount(1);
  });
});
