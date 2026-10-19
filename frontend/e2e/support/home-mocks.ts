/* eslint-disable @typescript-eslint/no-magic-numbers -- fixture figures */
import type {Page} from '@playwright/test';

import {mockAccounts} from './accounts-mocks';
import {API, AUTH_RESPONSE, mockApis as mockDossierApis} from './dossier-mocks';
import {json} from './visual-baseline';

const DASHBOARD = {
  aggregatedBalance: {USD: 50_000},
  totalNetWorthUsd: 50_000,
  baseCurrency: 'USD',
  accountCount: 3,
  accountsByType: {banking: 2, brokerage: 1},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
};

const HISTORY = [
  ['2026-09-01', 43_900, 5_300, 700],
  ['2026-09-15', 43_400, 5_600, 1_000],
].map(([snapshotDate, cash, brokerageInvested, cryptoInvested]) => ({
  snapshotDate,
  bankingTotal: cash,
  brokerageTotal: brokerageInvested,
  cryptoTotal: cryptoInvested,
  totalNetWorth: Number(cash) + Number(brokerageInvested) + Number(cryptoInvested),
  currency: 'USD',
  cashTotal: cash,
  brokerageInvested,
  cryptoInvested,
}));

/** Everything Home reads, over the shared accounts and holdings mocks; the caller opens `/dashboard`. */
export async function mockHome(page: Page): Promise<void> {
  await mockDossierApis(page);
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: HISTORY, hasHistory: true}))
  );
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await mockAccounts(page);
}
