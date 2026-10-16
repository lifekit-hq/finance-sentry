import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {type Signal, signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {ASYNC_STATE_SHOW_DELAY_MS, CmnDrawerService} from '@lifekit-hq/ui';
import {Subject} from 'rxjs';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';
import {TransactionLedgerComponent} from './transaction-ledger.component';

const ROW = {
  transactionId: 't1',
  accountId: 'a1',
  bankName: 'Monzo',
  currency: 'USD',
  amount: -5,
  amountUsd: -5,
  date: '2026-10-06T09:00:00Z',
  postedDate: null,
  description: 'Coffee',
  transactionType: 'debit',
  merchantCategory: null,
  isPending: false,
};

function setup(
  type: string | null,
  extra: Record<string, string | string[]> = {},
  rows: unknown[] = []
) {
  const drawerOpen = vi.fn(() => ({beforeClose$: new Subject<void>(), close: vi.fn()}));
  const store = {
    accounts: signal([]),
    monthlyOutflow: signal(null),
    monthlyOutflowCurrency: signal('USD'),
    topCategory: signal(null),
    errorMessage: signal(''),
    load: vi.fn(),
    retry: vi.fn(),
    lastSyncedAt: signal<number | null>(null),
    revalidating: signal(false),
    isLoading: signal(false),
    isEmpty: signal(true),
    hasActiveFilter: signal(false),
    hasMore: signal(false),
    isAppendLoading: signal(false),
    hasAppendError: signal(false),
    loadMore: vi.fn(),
    transactions: signal(rows as never[]),
    accountId: signal<string | null>((extra['account'] as string | undefined) ?? null),
    transactionType: signal<string | null>(type),
    categories: signal<string[]>([extra['category'] ?? []].flat()),
    from: signal<string | null>((extra['from'] as string | undefined) ?? null),
    to: signal<string | null>((extra['to'] as string | undefined) ?? null),
    search: signal((extra['q'] as string | undefined) ?? ''),
    applySearch: vi.fn(),
  };
  TestBed.overrideComponent(TransactionLedgerComponent, {
    set: {providers: [{provide: TransactionLedgerStore, useValue: store}]},
  });
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
      {provide: CmnDrawerService, useValue: {open: drawerOpen}},
      {
        provide: CategoryStore,
        useValue: {
          labelMap: signal({FAMILY_SUPPORT: 'Family Support', FOOD: 'Food'}),
          filterOptions: signal([]),
        },
      },
    ],
  });
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(TransactionLedgerComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const chip = (id: string) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement;
  return {chip, navigate, root, store, fixture, drawerOpen};
}

type ChipRowElement = HTMLElement & {selected: string[]};
type DismissibleChipElement = HTMLElement & {label: string};

function pressChip(row: HTMLElement, value: string): void {
  row.dispatchEvent(new CustomEvent('lk-chip-row-change', {detail: {selected: [value], value}}));
}

function appliedChips(root: HTMLElement): DismissibleChipElement[] {
  return Array.from(root.querySelectorAll<DismissibleChipElement>('lk-dismissible-chip'));
}

function appliedChip(root: HTMLElement, label: string): DismissibleChipElement {
  const found = appliedChips(root).find(c => c.label === label);
  if (!found) {
    throw new Error(`no applied chip "${label}"`);
  }
  return found;
}

function filtersButton(root: HTMLElement): HTMLButtonElement {
  return root.querySelector('cmn-filter-bar cmn-badge button') as HTMLButtonElement;
}

describe('TransactionLedgerComponent filter sheet', () => {
  it('opens the responsive Filters sheet from the filter bar', () => {
    const {root, drawerOpen} = setup(null);
    filtersButton(root).click();
    expect(drawerOpen).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({title: 'Filters', mode: 'responsive'})
    );
  });

  it('seeds the sheet with the filters in the store', () => {
    const {fixture} = setup('credit', {from: '2026-06-01', to: '2026-08-31', category: 'FOOD'});
    expect(fixture.componentInstance.activeSelection()).toEqual({
      from: '2026-06-01',
      to: '2026-08-31',
      type: 'credit',
      categories: ['FOOD'],
    });
  });

  it('writes the applied Period + Type + Category to the URL and keeps account and search', () => {
    const {navigate, fixture} = setup(null, {account: 'a1', q: 'x'});
    fixture.componentInstance.applySelection({
      from: '2026-08-01',
      to: '2026-08-12',
      type: 'debit',
      categories: ['FOOD', 'FAMILY_SUPPORT'],
    });
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {
        from: '2026-08-01',
        to: '2026-08-12',
        type: 'debit',
        category: ['FOOD', 'FAMILY_SUPPORT'],
      },
      queryParamsHandling: 'merge',
    });
  });
});

describe('TransactionLedgerComponent period chips', () => {
  it('writes the picked period dates to the URL and keeps the other params', () => {
    const {chip, navigate} = setup('debit', {account: 'a1'});
    pressChip(chip('period-chips'), 'last-month');
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: LedgerPeriodUtils.dates('last-month'),
      queryParamsHandling: 'merge',
    });
  });

  it('selects the chip whose dates are in the URL', () => {
    const {chip} = setup(null, {...LedgerPeriodUtils.dates('3m')});
    expect((chip('period-chips') as ChipRowElement).selected).toEqual(['3m']);
  });

  it('selects no period chip for a custom range', () => {
    const {chip} = setup(null, {from: '2020-01-01', to: '2020-01-31'});
    expect((chip('period-chips') as ChipRowElement).selected).toEqual([]);
  });
});

describe('TransactionLedgerComponent applied filter chips', () => {
  it('shows no chip row without filters', () => {
    const {root} = setup(null);
    expect(appliedChips(root)).toHaveLength(0);
  });

  it('shows a chip per period, type and category, dismissible one by one', () => {
    const {root, navigate} = setup('credit', {
      from: '2026-06-01',
      to: '2026-08-31',
      category: ['FAMILY_SUPPORT', 'FOOD'],
    });
    expect(appliedChips(root).map(c => c.label)).toEqual([
      'Dates: 2026-06-01 – 2026-08-31',
      'Type: In',
      'Category: Family Support',
      'Category: Food',
    ]);

    appliedChip(root, 'Category: Food').dispatchEvent(
      new CustomEvent('lk-dismissible-chip-remove')
    );
    expect(navigate).toHaveBeenLastCalledWith([], {
      queryParams: {
        from: '2026-06-01',
        to: '2026-08-31',
        type: 'credit',
        category: ['FAMILY_SUPPORT'],
      },
      queryParamsHandling: 'merge',
    });

    appliedChip(root, 'Type: In').dispatchEvent(new CustomEvent('lk-dismissible-chip-remove'));
    expect(navigate).toHaveBeenLastCalledWith([], {
      queryParams: expect.objectContaining({type: null}),
      queryParamsHandling: 'merge',
    });

    appliedChip(root, 'Dates: 2026-06-01 – 2026-08-31').dispatchEvent(
      new CustomEvent('lk-dismissible-chip-remove')
    );
    expect(navigate).toHaveBeenLastCalledWith([], {
      queryParams: expect.objectContaining({from: null, to: null}),
      queryParamsHandling: 'merge',
    });
  });

  it('names a quick period by its label', () => {
    const {from, to} = LedgerPeriodUtils.dates('last-month');
    const {root} = setup(null, {from, to});
    expect(appliedChips(root).map(c => c.label)).toEqual(['Period: Last month']);
  });

  it('Clear drops every sheet filter and leaves account and search alone', () => {
    const {root, navigate} = setup('debit', {account: 'a1', category: 'FOOD', from: '2026-06-01'});
    const clear = Array.from(
      root.querySelectorAll<HTMLButtonElement>('cmn-filter-bar button')
    ).find(b => b.textContent?.trim() === 'Clear');
    clear?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {from: null, to: null, type: null, category: null},
      queryParamsHandling: 'merge',
    });
  });

  it('counts the active filters on the filter button badge', () => {
    const {root} = setup('debit', {category: ['FOOD', 'FAMILY_SUPPORT'], from: '2026-06-01'});
    expect(root.querySelector('cmn-badge')?.textContent).toContain('4');
  });
});

describe('TransactionLedgerComponent account filter', () => {
  it('selects an account through the URL, pushing a history entry', () => {
    const {navigate, store, fixture, chip} = setup(null);
    store.accounts.set([
      {accountId: 'a1', label: 'Monzo'},
      {accountId: 'a2', label: 'AIB'},
    ] as never);
    fixture.detectChanges();

    pressChip(chip('account-chips'), 'a2');

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {account: 'a2'},
      queryParamsHandling: 'merge',
    });
  });

  it('clears the account filter from "All accounts"', () => {
    const {navigate, store, fixture, chip} = setup(null, {account: 'a1'});
    store.accounts.set([
      {accountId: 'a1', label: 'Monzo'},
      {accountId: 'a2', label: 'AIB'},
    ] as never);
    fixture.detectChanges();
    const row = chip('account-chips') as ChipRowElement;
    expect(row.selected).toEqual(['a1']);

    pressChip(row, 'all');

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {account: null},
      queryParamsHandling: 'merge',
    });
  });

  it('hides the account row with a single account', () => {
    const {store, fixture, root} = setup(null);
    store.accounts.set([{accountId: 'a1', label: 'Monzo'}] as never);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="account-chips"]')).toBeNull();
  });
});

describe('TransactionLedgerComponent search box', () => {
  const searchBox = (root: HTMLElement) =>
    root.querySelector('input[type="search"]') as HTMLInputElement;

  it('starts from the search the store holds', async () => {
    const {root, fixture} = setup(null, {q: 'netflix'});
    await fixture.whenStable();
    fixture.detectChanges();
    expect(searchBox(root).value).toBe('netflix');
  });

  it('feeds typed text to the store', () => {
    const {root, store} = setup(null);
    const feed = store.applySearch.mock.calls[0][0] as Signal<string>;

    const input = searchBox(root);
    input.value = 'spotify';
    input.dispatchEvent(new Event('input'));

    expect(feed()).toBe('spotify');
  });

  it('never writes the URL itself', () => {
    const {root, navigate} = setup(null);
    const input = searchBox(root);
    input.value = 'spotify';
    input.dispatchEvent(new Event('input'));
    expect(navigate).not.toHaveBeenCalled();
  });

  it('restores the box when the store search changes (back/forward)', async () => {
    const {root, store, fixture} = setup(null, {q: 'netflix'});
    store.search.set('spotify');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(searchBox(root).value).toBe('spotify');
  });

  it('labels each day as a list section', () => {
    const {store, fixture, root} = setup(null);
    store.transactions.set([ROW] as never);
    store.isEmpty.set(false);
    fixture.detectChanges();
    expect(root.querySelectorAll('cmn-list-section')).toHaveLength(1);
    expect(root.querySelector('cmn-list-section section')?.getAttribute('aria-labelledby')).toBe(
      root.querySelector('cmn-list-section h2 span')?.id
    );
  });
});

describe('TransactionLedgerComponent rows', () => {
  it('opens the drawer from a native button stretched over the row', () => {
    const {store, fixture, root} = setup(null);
    store.transactions.set([ROW] as never);
    store.isEmpty.set(false);
    const openDrawer = vi.spyOn(fixture.componentInstance, 'openDrawer').mockReturnValue();
    fixture.detectChanges();

    const row = root.querySelector('[data-testid="ledger-row"]');
    const open = row?.querySelector<HTMLButtonElement>('button[data-testid="ledger-row-open"]');
    expect(row?.hasAttribute('role')).toBe(false);
    expect(open?.type).toBe('button');
    expect(open?.getAttribute('aria-label')).toBe('Coffee');

    open?.click();
    expect(openDrawer).toHaveBeenCalledTimes(1);
  });
});

describe('TransactionLedgerComponent async states', () => {
  it('shows the section skeleton, once the show delay has passed, while loading', async () => {
    const {store, fixture, root} = setup(null);
    store.isEmpty.set(false);
    store.isLoading.set(true);
    fixture.detectChanges();

    // A load under the show delay draws no indicator, so a fast response never flashes one.
    expect(root.querySelector('cmn-skeleton')).toBeNull();
    await new Promise(resolve => setTimeout(resolve, ASYNC_STATE_SHOW_DELAY_MS + 50));
    fixture.detectChanges();

    expect(root.querySelectorAll('cmn-skeleton[shape="section"]').length).toBeGreaterThan(0);
    expect(root.querySelector('[data-testid="ledger-row"]')).toBeNull();
  });

  it('words the empty state differently with and without an active filter', () => {
    const {store, fixture, root} = setup(null);
    fixture.detectChanges();
    expect(root.textContent).toContain('No transactions found');

    store.hasActiveFilter.set(true);
    fixture.detectChanges();
    expect(root.textContent).toContain('No matching transactions');
  });

  it('shows the error with a Retry that reloads the ledger', () => {
    const {store, fixture, root} = setup(null);
    store.errorMessage.set('Failed to load transactions.');
    fixture.detectChanges();

    expect(root.querySelector('cmn-alert')?.textContent).toContain('Failed to load transactions.');
    root.querySelector<HTMLElement>('cmn-alert cmn-button button')?.click();
    expect(store.retry).toHaveBeenCalledOnce();
  });

  it('never renders an error as the empty state', () => {
    const {store, fixture, root} = setup(null);
    store.errorMessage.set('Failed to load transactions.');
    store.isEmpty.set(false);
    fixture.detectChanges();

    expect(root.textContent).not.toContain('No transactions found');
  });

  it('paints the rows a revisit restored at once while they revalidate', () => {
    const {store, fixture, root} = setup(null, {}, [ROW]);
    store.isEmpty.set(false);
    store.revalidating.set(true);
    store.isLoading.set(true);
    fixture.detectChanges();

    expect(root.querySelector('cmn-skeleton')).toBeNull();
    expect(root.querySelectorAll('[data-testid="ledger-row"]')).toHaveLength(1);
    expect(root.querySelector('.animate-cmn-fade-in')).toBeNull();
  });

  it('fades the first rows in where they replace the skeleton', () => {
    const {store, fixture, root} = setup(null);
    store.transactions.set([ROW] as never);
    store.isEmpty.set(false);
    fixture.detectChanges();

    expect(root.querySelector('.animate-cmn-fade-in [data-testid="ledger-row"]')).not.toBeNull();
  });

  it('keeps the loaded rows on screen through a reload', async () => {
    const {store, fixture, root} = setup(null);
    store.transactions.set([ROW] as never);
    store.isEmpty.set(false);
    fixture.detectChanges();
    store.isLoading.set(true);
    fixture.detectChanges();
    await new Promise(resolve => setTimeout(resolve, ASYNC_STATE_SHOW_DELAY_MS + 50));
    fixture.detectChanges();

    expect(root.querySelector('cmn-skeleton')).toBeNull();
    expect(root.querySelectorAll('[data-testid="ledger-row"]')).toHaveLength(1);
  });

  it('shows the last data with a last-synced notice instead of an error while offline', () => {
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
    const {store, fixture, root} = setup(null);
    store.transactions.set([ROW] as never);
    store.isEmpty.set(false);
    store.lastSyncedAt.set(Date.now());
    store.errorMessage.set('Failed to load transactions.');
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="offline-notice"]')?.textContent).toContain(
      "You're offline"
    );
    expect(root.textContent).not.toContain('Failed to load transactions.');
    expect(root.querySelectorAll('[data-testid="ledger-row"]')).toHaveLength(1);
    vi.restoreAllMocks();
  });
});
