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
    await page.goto('/accounts/list');
    const broker = page.locator('cmn-disclosure-row', {hasText: 'Interactive Brokers'});
    await broker.locator('summary').click();
    // The whole position row is the click target
    await broker.getByTestId('position-row').filter({hasText: 'AAPL'}).click();

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
      performance: null,
      position: {
        provider: 'binance',
        assetClass: 'Crypto',
        quantity: 0.5,
        currentValueUsd: 30000,
        costBasisUsd: null,
        unrealizedPnlUsd: null,
        unrealizedPnlPercent: null,
        shareOfBookPercent: null,
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
    await page.waitForLoadState('networkidle');
    await expect(page.getByTestId('dossier-price')).toHaveCount(0);
    expect(quoteRequests).toEqual([]);
  });

  test('dossier hero opens with the company, quote slot and position strip', async ({page}) => {
    await page.goto('/assets/AAPL');
    const hero = page.getByTestId('dossier-hero');
    await expect(hero.getByTestId('dossier-name')).toHaveText('Apple Inc.');
    await expect(hero.getByTestId('dossier-profile')).toContainText(
      'AAPL · Technology · Consumer Electronics'
    );
    const strip = hero.getByTestId('position-strip');
    await expect(strip.getByTestId('position-value')).toContainText('$17,500.00');
    await expect(strip.getByTestId('position-pnl')).toContainText('$2,500.00');
    await expect(strip.getByTestId('position-pnl')).toContainText('16.67%');
    await expect(strip.getByTestId('position-share')).toContainText('12.5%');
    await expect(strip.getByTestId('position-provider-link')).toBeVisible();
  });

  test('section chips jump to their section', async ({page}) => {
    await page.goto('/assets/AAPL');
    const chips = page.getByTestId('dossier-chips');
    await expect(chips).toBeVisible();

    await chips.getByRole('button', {name: 'News'}).click();

    await expect(page.locator('#recent-news')).toBeInViewport();
  });

  test('dossier page writes enums in words and renders yield and peers', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Target changed')).toBeVisible();
    await expect(page.locator('#recent-news').getByText('Yahoo Finance')).toBeVisible();
    await expect(page.getByText('yahoo:AAPL')).toHaveCount(0);
    const dividend = page.getByTestId('fundamental-dividend-yield');
    await expect(dividend).toContainText('Dividend yield');
    await expect(dividend).toContainText('0.50%');
    await expect(page.getByTestId('valuation-peers')).toContainText('Versus Technology peers');
    await expect(page.getByTestId('valuation-peer')).toHaveCount(2);
  });

  test('dossier page renders thesis section', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: "What's the plan?"})).toBeVisible();
    await expect(page.getByText('Apple continues to expand its services revenue')).toBeVisible();
    await expect(page.getByText('Active', {exact: true})).toBeVisible();
  });

  test('dossier page renders analyst coverage', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: 'What do analysts say?'})).toBeVisible();
    await expect(page.getByText('Goldman Sachs')).toBeVisible();
  });

  test('dossier page renders recent news', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Recent News')).toBeVisible();
    await expect(page.getByText('Apple reports record services revenue in Q3')).toBeVisible();
  });

  test('the top bar back chevron returns to accounts', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();

    await page.getByRole('button', {name: 'Back'}).click();

    await expect(page).toHaveURL(/\/accounts/);
  });

  test("the dossier's provider link opens that institution's accordion on Accounts", async ({
    page,
  }) => {
    await page.goto('/assets/AAPL');

    await page.getByTestId('position-provider-link').click();

    await expect(page).toHaveURL(/\/accounts\/list\?provider=ibkr/);
    const broker = page.locator('cmn-disclosure-row', {hasText: 'Interactive Brokers'});
    await expect(broker.locator('details').first()).toHaveJSProperty('open', true);
    await expect(broker.getByTestId('position-row').first()).toBeVisible();
    await expect(broker.getByTestId('institution-investments-link')).toHaveCount(0);
  });

  test('accounts list position row opens the asset dossier', async ({page}) => {
    await page.goto('/accounts/list');
    const broker = page.locator('cmn-disclosure-row', {hasText: 'Interactive Brokers'});
    await broker.locator('summary').click();

    await broker.getByTestId('position-row').first().click();

    await expect(page).toHaveURL(/\/assets\//);
  });

  test('dossier page keeps tax lots collapsed and shows triggers as sentences', async ({page}) => {
    await page.goto('/assets/AAPL');
    const lots = page.getByTestId('tax-lots');
    await expect(lots).toBeVisible();
    await expect(lots).not.toHaveAttribute('open', '');
    await expect(lots.locator('summary')).toContainText('Tax lots (');
    await expect(page.getByText('Revenue growth (YoY) falls below 5.0%')).toBeVisible();
  });

  test('dossier page renders the recommendation trend as one verdict and bar', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Recommendation Trend')).toBeVisible();
    await expect(page.getByTestId('recommendation-verdict')).toContainText(
      '30 of 36 analysts rate it Buy'
    );
    await expect(page.getByTestId('recommendation-bar')).toBeVisible();
    await expect(page.getByTestId('trend-list')).toContainText('Strong buy');
    await expect(page.getByTestId('trend-list')).toContainText('18');
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
    await expect(page.getByRole('heading', {name: "What's the plan?"})).toHaveCount(0);
    await expect(page.getByText('Radar Signals')).toHaveCount(0);
    await expect(page.getByTestId('ledger-read-card')).toHaveCount(0);
  });
});

test.describe('Asset Dossier - chart, checks and returns', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('hero carries the thesis hook, the performance strip and the size against the cap', async ({
    page,
  }) => {
    await page.goto('/assets/AAPL');
    const hero = page.getByTestId('dossier-hero');
    await expect(hero.getByTestId('dossier-hook')).toContainText('12.5% of book');
    await expect(hero.getByTestId('performance-strip')).toContainText('+22.2%');
    await expect(hero.getByTestId('performance-strip')).toContainText('from 52w high');
    await expect(hero.getByTestId('size-cap-text')).toContainText('10');
    await expect(hero.getByTestId('size-cap-mark')).toBeVisible();
  });

  test('checks sit next to the wheel and open to their rules and values', async ({page}) => {
    await page.goto('/assets/AAPL');
    const checks = page.getByTestId('dossier-checks');
    await expect(checks.getByTestId('checks-wheel')).toBeVisible();
    await expect(checks).toContainText('6 of 10 pass');
    const value = checks.getByTestId('check-axis-value');
    await value.locator('summary').click();
    await expect(value).toContainText('P/E below its own 5-year average');
    await expect(value).toContainText('28.5x vs 25.3x');
    await expect(value).toContainText('median of 2 peers');
  });

  test('chart draws the year with the S&P 500, trades and a range switch', async ({page}) => {
    await page.goto('/assets/AAPL');
    const chart = page.getByTestId('price-chart');
    await expect(chart.locator('canvas')).toBeVisible();
    await expect(chart.getByTestId('chart-trade')).toHaveCount(3);
    await expect(chart.getByTestId('chart-key')).toContainText('Your average cost $150.00');

    const request = page.waitForRequest(req => req.url().includes('/chart?range=5Y'));
    await chart.getByTestId('chart-ranges').getByText('5Y', {exact: true}).click();
    await request;
  });

  test('return decomposition splits unrealized, realized, dividends and currency', async ({
    page,
  }) => {
    await page.goto('/assets/AAPL');
    const returns = page.getByTestId('returns');
    await expect(returns.getByTestId('returns-total')).toContainText('$3,067');
    await expect(returns.getByTestId('returns-realized')).toContainText('$519');
    await expect(returns.getByTestId('returns-dividends')).toContainText('$48');
    await expect(returns.getByTestId('returns-currency')).toContainText('Not available');
  });

  test('"Is it cheap?" names the analyst source and saves a target of your own', async ({page}) => {
    await page.goto('/assets/AAPL');
    const valuation = page.getByTestId('valuation');
    await expect(valuation.getByTestId('target-range')).toBeVisible();
    await expect(valuation.getByTestId('target-source')).toContainText('36 analysts');
    await expect(valuation.getByTestId('target-marker-mine')).toHaveCount(0);

    await valuation.getByTestId('my-target-edit').getByRole('button').click();
    await valuation.getByTestId('my-target-input').locator('input').fill('230');
    const saved = page.waitForRequest(
      req => req.url().endsWith('/research/assets/AAPL/target') && req.method() === 'PUT'
    );
    await valuation.getByTestId('my-target-save').getByRole('button').click();
    expect((await saved).postDataJSON()).toEqual({targetPrice: 230, currency: 'USD'});

    await expect(valuation.getByTestId('my-target-value')).toHaveText('$230.00');
    await expect(valuation.getByTestId('target-marker-mine')).toBeVisible();

    await valuation.getByTestId('my-target-clear').getByRole('button').click();
    await expect(valuation.getByTestId('my-target-value')).toHaveCount(0);
  });

  test('ownership is marked not available and the data status names each source', async ({
    page,
  }) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByTestId('ownership')).toContainText('Not available');
    await expect(page.getByTestId('data-status-chart')).toContainText('Yahoo Finance');
    await expect(page.getByTestId('data-status-returns')).toContainText('3 fills');
  });

  test('a crypto holding keeps the price and your own target on the band, with analysts, fundamentals, peers and ownership hidden', async ({
    page,
  }) => {
    await page.goto('/assets/BTC');
    await expect(page.getByRole('heading', {name: 'BTC', level: 1})).toBeVisible();
    await expect(page.getByTestId('price-chart').locator('canvas')).toBeVisible();
    await expect(page.getByTestId('check-axis-momentum')).toBeVisible();
    await expect(page.getByTestId('dossier-checks')).toContainText('3 of 4 pass');
    await expect(page.getByTestId('check-axis-value')).toHaveCount(0);
    await expect(page.getByTestId('checks-wheel')).toHaveCount(0);
    await expect(page.getByTestId('valuation')).toContainText('Price & your target');
    await expect(page.getByTestId('target-band')).toBeVisible();
    await expect(page.getByTestId('my-target')).toBeVisible();
    await expect(page.getByTestId('target-marker-analyst')).toHaveCount(0);
    await expect(page.getByTestId('target-source')).toHaveCount(0);
    await expect(page.getByTestId('fundamentals')).toHaveCount(0);
    await expect(page.getByTestId('valuation-peers')).toHaveCount(0);
    await expect(page.getByTestId('recommendation-verdict')).toHaveCount(0);
    await expect(page.getByTestId('ownership')).toHaveCount(0);
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
