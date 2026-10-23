import {expect, type Page, test} from '@playwright/test';

import {API, mockApis} from './support/dossier-mocks';

interface Item {
  id: string;
  ticker: string;
  exchange: string | null;
  note: string | null;
  addedAt: string;
}

const LONG_NOTE =
  'Add on a pullback to the 50-day average if US commercial revenue growth holds above 30% for a second quarter, and skip if guidance is cut. '
    .repeat(3)
    .trim();

const PLTR: Item = {
  id: 'w1',
  ticker: 'PLTR',
  exchange: 'NASDAQ',
  note: LONG_NOTE,
  addedAt: '2026-10-14T09:00:00Z',
};

async function mockWatchlist(page: Page, initial: Item[]): Promise<{removed: string[]}> {
  const items = [...initial];
  const removed: string[] = [];
  await page.route(`${API}/research/watchlist`, route => {
    const request = route.request();
    if (request.method() === 'GET') {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(items),
      });
    }
    const body = request.postDataJSON() as {ticker: string; exchange?: string; note?: string};
    if (items.some(i => i.ticker === body.ticker)) {
      return route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({errorCode: 'WATCHLIST_ITEM_ALREADY_EXISTS'}),
      });
    }
    const created: Item = {
      id: `w${items.length + 10}`,
      ticker: body.ticker,
      exchange: body.exchange ?? null,
      note: body.note ?? null,
      addedAt: '2026-10-15T09:00:00Z',
    };
    items.push(created);
    return route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify(created),
    });
  });
  await page.route(`${API}/research/watchlist/*`, route => {
    const id = route.request().url().split('/').pop() ?? '';
    removed.push(id);
    items.splice(
      items.findIndex(i => i.id === id),
      1
    );
    return route.fulfill({status: 204});
  });
  return {removed};
}

test.describe('Watchlist', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('shows the empty state when nothing is watched', async ({page}) => {
    await mockWatchlist(page, []);
    await page.goto('/watchlist');
    await expect(page.getByTestId('watchlist-form')).toBeVisible();
    await expect(page.getByTestId('watchlist-row')).toHaveCount(0);
    await expect(page.getByText(/your watchlist is empty/i)).toBeVisible();
    await expect(page.getByTestId('watchlist-count')).toHaveCount(0);
  });

  test('counts the names and names each icon-only remove button after its ticker', async ({
    page,
  }) => {
    await mockWatchlist(page, [PLTR]);
    await page.goto('/watchlist');
    await expect(page.getByTestId('watchlist-count')).toHaveText(/watching\s*·\s*1/i);
    await expect(page.getByRole('button', {name: 'Remove PLTR'})).toBeVisible();
  });

  test('lists the full note and links the ticker to its dossier', async ({page}) => {
    await mockWatchlist(page, [PLTR]);
    await page.goto('/watchlist');
    await expect(page.getByTestId('watchlist-row-note')).toHaveText(LONG_NOTE);
    const link = page.getByTestId('watchlist-ticker-link');
    await expect(link).toHaveText('PLTR');
    await link.click();
    await expect(page).toHaveURL(/\/assets\/PLTR/);
  });

  test('adds an item, refuses a duplicate keeping what was typed, and removes after confirming', async ({
    page,
  }) => {
    const server = await mockWatchlist(page, [PLTR]);
    await page.goto('/watchlist');
    await expect(page.getByTestId('watchlist-row')).toHaveCount(1);

    await page.getByTestId('watchlist-ticker').locator('input').fill('MRNA');
    await page.getByTestId('watchlist-note').locator('textarea').fill('Wait for the Q3 print');
    await page.getByTestId('watchlist-add').click();
    await expect(page.getByTestId('watchlist-row')).toHaveCount(2);
    await expect(page.getByTestId('watchlist-row-note').first()).toBeVisible();

    await page.getByTestId('watchlist-ticker').locator('input').fill('PLTR');
    await page.getByTestId('watchlist-note').locator('textarea').fill('typed note');
    await page.getByTestId('watchlist-add').click();
    await expect(page.getByTestId('watchlist-action-error')).toContainText(
      'already on your watchlist'
    );
    await expect(page.getByTestId('watchlist-row')).toHaveCount(2);
    await expect(page.getByTestId('watchlist-note').locator('textarea')).toHaveValue('typed note');

    await page.getByTestId('watchlist-remove').first().click();
    await page.getByRole('dialog').getByRole('button', {name: 'Keep'}).click();
    await expect(page.getByTestId('watchlist-row')).toHaveCount(2);

    await page.getByTestId('watchlist-remove').first().click();
    await page.getByRole('dialog').getByRole('button', {name: 'Remove'}).click();
    await expect(page.getByTestId('watchlist-row')).toHaveCount(1);
    expect(server.removed).toHaveLength(1);
  });

  test('does not send a ticker or exchange over the backend column limits', async ({page}) => {
    const requests: string[] = [];
    page.on('request', r => {
      if (r.method() === 'POST' && r.url().includes('/research/watchlist')) {
        requests.push(r.url());
      }
    });
    await mockWatchlist(page, []);
    await page.goto('/watchlist');
    await page.getByTestId('watchlist-ticker').locator('input').fill('A'.repeat(21));
    await page.getByTestId('watchlist-add').click();
    await expect(page.getByTestId('watchlist-row')).toHaveCount(0);
    await page.getByTestId('watchlist-ticker').locator('input').fill('PLTR');
    await page.getByTestId('watchlist-exchange').locator('input').fill('B'.repeat(21));
    await page.getByTestId('watchlist-add').click();
    await expect(page.getByTestId('watchlist-row')).toHaveCount(0);
    expect(requests).toHaveLength(0);

    await page.getByTestId('watchlist-exchange').locator('input').fill('NASDAQ');
    await page.getByTestId('watchlist-add').click();
    await expect(page.getByTestId('watchlist-row')).toHaveCount(1);
    expect(requests).toHaveLength(1);
  });
});
