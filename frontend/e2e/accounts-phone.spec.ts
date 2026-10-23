import {expect, type Locator, type Page, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {API, mockApis} from './support/dossier-mocks';
import {mockGet} from './support/visual-baseline';

const PHONES = {
  'iPhone SE 375x812': {width: 375, height: 812},
  'iPhone 390x844': {width: 390, height: 844},
  'iPhone Plus 402x874': {width: 402, height: 874},
} as const;
const THREE_DAYS_MS = 3 * 24 * 60 * 60 * 1000;
const FROZEN_SINCE = new Date(Date.now() - THREE_DAYS_MS).toISOString();
const SYNCED_AT = new Date().toISOString();
const MAX_SCREENS = 2;
const HIT_TARGET_HALF_PX = 22;
const PIN_SCROLL_PX = 40;

function position(
  id: string,
  symbol: string,
  quantity: number,
  value: number,
  category: 'brokerage' | 'crypto',
  provider: string
): Record<string, unknown> {
  return {
    accountId: id,
    bankName: provider,
    accountType: category,
    accountNumberLast4: symbol.slice(0, 4),
    currency: symbol,
    symbol,
    provider,
    category,
    currentBalance: quantity,
    balanceInBaseCurrency: value,
    syncStatus: 'synced',
    lastSyncTimestamp: SYNCED_AT,
  };
}

function institution(
  id: string,
  provider: string,
  name: string,
  category: 'banking' | 'brokerage' | 'crypto',
  total: number,
  accounts: Record<string, unknown>[],
  syncStatus = 'synced'
): Record<string, unknown> {
  return {
    institutionId: id,
    provider,
    name,
    category,
    totalInBaseCurrency: total,
    syncStatus,
    lastSyncTimestamp: SYNCED_AT,
    lastSuccessfulSyncTimestamp: syncStatus === 'synced' ? SYNCED_AT : FROZEN_SINCE,
    accounts,
    cards: null,
  };
}

const AIB_BALANCE = 2400;
const GOOGL_VALUE = 6800;
const BTC_VALUE = 3200;

const SUMMARY = {
  totalNetWorth: AIB_BALANCE + GOOGL_VALUE + BTC_VALUE,
  baseCurrency: 'USD',
  appliedFilters: {category: null, provider: null},
  categories: [
    {
      category: 'banking',
      totalInBaseCurrency: AIB_BALANCE,
      institutionCount: 1,
      institutions: [
        institution(
          'aib-1',
          'truelayer',
          'AIB',
          'banking',
          AIB_BALANCE,
          [
            {
              accountId: 'acc-aib',
              bankName: 'AIB',
              accountType: 'current',
              accountNumberLast4: '4821',
              currency: 'USD',
              provider: 'truelayer',
              category: 'banking',
              currentBalance: AIB_BALANCE,
              balanceInBaseCurrency: AIB_BALANCE,
              syncStatus: 'reauth_required',
              lastSyncTimestamp: FROZEN_SINCE,
            },
          ],
          'reauth_required'
        ),
      ],
    },
    {
      category: 'brokerage',
      totalInBaseCurrency: GOOGL_VALUE,
      institutionCount: 1,
      institutions: [
        institution('ibkr-1', 'ibkr', 'Interactive Brokers', 'brokerage', GOOGL_VALUE, [
          position('p-googl', 'GOOGL', 40, GOOGL_VALUE, 'brokerage', 'ibkr'),
        ]),
      ],
    },
    {
      category: 'crypto',
      totalInBaseCurrency: BTC_VALUE,
      institutionCount: 1,
      institutions: [
        institution('binance-1', 'binance', 'Binance', 'crypto', BTC_VALUE, [
          position('p-btc', 'BTC', 0.0812, BTC_VALUE, 'crypto', 'binance'),
        ]),
      ],
    },
  ],
};

const MANY_IBKR = 14;
const MANY_BINANCE = 9;

// The shape that made the page long: a broker and an exchange with a full portfolio each.
function heavySummary(): typeof SUMMARY {
  const positions = (count: number, prefix: string, category: 'brokerage' | 'crypto', p: string) =>
    Array.from({length: count}, (_, i) =>
      position(`${prefix}-${i}`, `${prefix}${i}`.toUpperCase(), 1, 100, category, p)
    );
  const [banking, brokerage, crypto] = SUMMARY.categories;
  return {
    ...SUMMARY,
    categories: [
      banking,
      {
        ...brokerage,
        institutions: [
          institution(
            'ibkr-1',
            'ibkr',
            'Interactive Brokers',
            'brokerage',
            MANY_IBKR * 100,
            positions(MANY_IBKR, 'sym', 'brokerage', 'ibkr')
          ),
        ],
      },
      {
        ...crypto,
        institutions: [
          institution(
            'binance-1',
            'binance',
            'Binance',
            'crypto',
            MANY_BINANCE * 100,
            positions(MANY_BINANCE, 'coin', 'crypto', 'binance')
          ),
        ],
      },
    ],
  };
}

// The shell's page-overflow spacer (a transparent block after the page frame) sits over the padding of
// library rows that overflow the frame, so a tap lands on the row's label text, as a thumb does.
/** A disclosure row's own toggle: it names its body (`aria-controls`) and states `aria-expanded`. */
const ROW_TOGGLE = 'cmn-disclosure-row button[aria-controls]';

async function toggle(row: Locator, name: string): Promise<void> {
  await row.getByText(name, {exact: true}).first().click();
}

async function openAccounts(page: Page, summary: unknown = SUMMARY): Promise<void> {
  await mockApis(page);
  await mockAccounts(page);
  await mockGet(page, 'wealth/summary', summary);
  await page.route(`${API}/alerts/unread-count`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify({count: 0})})
  );
  await page.goto('/accounts/list');
}

for (const [name, viewport] of Object.entries(PHONES)) {
  test.describe(`accounts on a phone (${name})`, () => {
    test.use({viewport, hasTouch: true, isMobile: true});

    test('puts the reconnect row first, with a visible Reconnect button', async ({page}) => {
      await openAccounts(page);

      const row = page.getByTestId('attention-row');
      await expect(row).toBeVisible();
      await expect(row).toContainText('AIB needs reconnecting');
      await expect(row).toContainText('Balances frozen since 3d ago');
      await expect(row.getByRole('button', {name: 'Reconnect'})).toBeVisible();
    });

    test('shows one disclosure row per institution, with a status dot on every avatar and no sideways scroll', async ({
      page,
    }) => {
      await openAccounts(page);

      await expect(page.getByTestId('section-subtotal').first()).toBeVisible();
      const rows = page.locator('cmn-disclosure-row');
      await expect(rows).toHaveCount(3);
      await expect(rows.nth(1)).toContainText('Interactive Brokers');
      await expect(rows.nth(1)).toContainText('1 position');
      await expect(rows.nth(1).getByTestId('institution-total')).toHaveText('$6,800.00');
      await expect(rows.nth(2)).toContainText('1 asset');
      await expect(page.getByTestId('avatar-status')).toHaveCount(3);
      // Only the broken bank spells its state out; healthy rows rely on the dot alone.
      await expect(rows.nth(0)).toContainText('Reconnect needed');
      await expect(rows.nth(1)).not.toContainText('Synced');

      const overflow = await page
        .locator('cmn-page-scroll')
        .evaluate(el => el.scrollWidth - el.clientWidth);
      expect(overflow).toBeLessThanOrEqual(0);
    });

    test('opens the bank and keeps the broker and exchange closed on a first visit', async ({
      page,
    }) => {
      await openAccounts(page);

      const groups = page.locator(ROW_TOGGLE);
      await expect(groups.nth(0)).toHaveAttribute('aria-expanded', 'true');
      await expect(groups.nth(1)).toHaveAttribute('aria-expanded', 'false');
      await expect(groups.nth(2)).toHaveAttribute('aria-expanded', 'false');
    });

    test('opens a broker in place with value-first positions and no link out', async ({page}) => {
      await openAccounts(page);

      const broker = page.locator('cmn-disclosure-row').nth(1);
      await toggle(broker, 'Interactive Brokers');

      const row = broker.getByTestId('position-row').first();
      await expect(row).toBeVisible();
      await expect(row.getByTestId('position-ticker')).toHaveText('MSFT');
      await expect(row.getByTestId('position-quantity')).toHaveText('5');
      await expect(row.getByTestId('position-value')).toHaveText('$21,000.00');
      await expect(
        broker.getByRole('button', {name: 'Actions for Interactive Brokers'})
      ).toBeVisible();

      await expect(broker.getByTestId('institution-investments-link')).toHaveCount(0);
    });

    test('gives every actions menu a 44px hit target without growing the row', async ({page}) => {
      await openAccounts(page);

      const menus = page.getByRole('button', {name: /^Actions for /});
      await expect(menus).toHaveCount(3);
      for (const menu of await menus.all()) {
        await menu.scrollIntoViewIfNeeded();
        // The bottom tab bar overlays the content, so the menu has to be scrolled clear of it
        // (not merely into the viewport) before its whole 44px target can be probed.
        await menu.evaluate(el => el.scrollIntoView({block: 'center'}));
        const box = await menu.boundingBox();
        const covered = await menu.evaluate((el, edge) => {
          const r = el.getBoundingClientRect();
          const cx = r.x + r.width / 2;
          const cy = r.y + r.height / 2;
          return [
            [-edge, 0],
            [edge, 0],
            [0, -edge],
            [0, edge],
          ].every(([dx, dy]) => {
            const hit = document.elementFromPoint(cx + (dx ?? 0), cy + (dy ?? 0));
            return hit !== null && el.contains(hit);
          });
        }, HIT_TARGET_HALF_PX - 1);
        expect(covered).toBe(true);
        expect(box?.height).toBeLessThan(HIT_TARGET_HALF_PX * 2);
      }
    });

    test('keeps the section header pinned while its rows scroll under it', async ({page}) => {
      await openAccounts(page, heavySummary());
      const header = page.locator('cmn-list-section h2').first();
      await expect(header).toBeVisible();

      const region = page.locator('cmn-page-scroll');
      const pinnedAt = async (scrollTop: number): Promise<number> => {
        const reached = await region.evaluate((el, top) => {
          el.scrollTop = top;
          return el.scrollTop;
        }, scrollTop);
        expect(reached).toBeCloseTo(scrollTop, 0);
        return (await header.boundingBox())?.y ?? -1;
      };
      const regionTop = (await region.boundingBox())?.y ?? 0;
      const naturalY = await pinnedAt(0);
      const offset = Math.round(naturalY - regionTop);
      const before = await pinnedAt(offset);
      const after = await pinnedAt(offset + PIN_SCROLL_PX);
      expect(before).toBeLessThan(naturalY - PIN_SCROLL_PX);
      expect(Math.abs(after - before)).toBeLessThanOrEqual(1);
    });

    test('remembers what was opened or closed across a reload, subtotal still in the header', async ({
      page,
    }) => {
      await openAccounts(page);
      const groups = page.locator(ROW_TOGGLE);
      await expect(groups.nth(0)).toHaveAttribute('aria-expanded', 'true');

      await toggle(groups.nth(0), 'AIB');
      await toggle(groups.nth(1), 'Interactive Brokers');
      await expect(groups.nth(0)).toHaveAttribute('aria-expanded', 'false');
      await expect(groups.nth(1)).toHaveAttribute('aria-expanded', 'true');
      await expect
        .poll(() => page.evaluate(() => localStorage.getItem('fns.accounts.institutionOpen')))
        .toBe('{"truelayer:aib-1":false,"ibkr:ibkr-1":true}');
      await page.reload();

      await expect(groups.nth(0)).toHaveAttribute('aria-expanded', 'false');
      await expect(groups.nth(1)).toHaveAttribute('aria-expanded', 'true');
      await expect(groups.nth(2)).toHaveAttribute('aria-expanded', 'false');
      await expect(page.getByTestId('section-subtotal').first()).toBeVisible();
    });

    test('keeps a 23-position portfolio within two screens, closed until opened', async ({
      page,
    }) => {
      await openAccounts(page, heavySummary());
      await expect(page.locator('cmn-disclosure-row').nth(1)).toBeVisible();

      const screens = await page.evaluate(
        () => document.documentElement.scrollHeight / window.innerHeight
      );
      expect(screens).toBeLessThanOrEqual(MAX_SCREENS);
    });
  });
}
