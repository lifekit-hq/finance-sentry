import {expect} from '@playwright/test';

import {describeVisualBaseline, mockGet} from './support/visual-baseline';

const ITEMS = [
  {
    id: 'w1',
    ticker: 'PLTR',
    exchange: 'NASDAQ',
    note: 'Add on a pullback to the 50-day average if US commercial revenue growth holds above 30% for a second quarter, and skip if guidance is cut.',
    addedAt: '2026-09-02T09:00:00Z',
  },
  {
    id: 'w2',
    ticker: 'MRNA',
    exchange: null,
    note: 'Wait for the Q3 print',
    addedAt: '2026-08-21T09:00:00Z',
  },
  {
    id: 'w3',
    ticker: 'ASML',
    exchange: 'AEX',
    note: null,
    addedAt: '2026-07-11T09:00:00Z',
  },
];

describeVisualBaseline({
  name: 'watchlist',
  title: 'Watchlist',
  path: '/watchlist',
  mocks: async page => {
    await mockGet(page, 'research/watchlist', ITEMS);
  },
  ready: async page => {
    await expect(page.getByText('PLTR').first()).toBeVisible();
  },
  desktopHeight: 900,
  phoneHeight: 1100,
});

describeVisualBaseline({
  name: 'watchlist-empty',
  title: 'Watchlist (empty)',
  path: '/watchlist',
  mocks: async page => {
    await mockGet(page, 'research/watchlist', []);
  },
  ready: async page => {
    await expect(page.getByText(/your watchlist is empty/i)).toBeVisible();
  },
  desktopHeight: 900,
  phoneHeight: 1100,
});
