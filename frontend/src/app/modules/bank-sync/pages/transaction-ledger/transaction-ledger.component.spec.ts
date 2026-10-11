import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';

import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
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

function setup(type: string | null, extra: Record<string, string> = {}) {
  const store = {
    accounts: signal([]),
    monthlyOutflow: signal(null),
    monthlyOutflowCurrency: signal('USD'),
    topCategory: signal(null),
    errorMessage: signal(''),
    load: vi.fn(),
    retry: vi.fn(),
    lastSyncedAt: signal<number | null>(null),
    isLoading: signal(false),
    isEmpty: signal(true),
    hasActiveFilter: signal(false),
    hasMore: signal(false),
    transactions: signal([]),
    accountId: signal<string | null>(extra['account'] ?? null),
    transactionType: signal<string | null>(type),
    category: signal<string | null>(extra['category'] ?? null),
    from: signal<string | null>(extra['from'] ?? null),
    to: signal<string | null>(extra['to'] ?? null),
    search: signal(extra['q'] ?? ''),
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
    ],
  });
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(TransactionLedgerComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const chip = (id: string) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement;
  const selected = (id: string) => chip(id).querySelector('button')?.getAttribute('aria-pressed');
  return {chip, selected, navigate, root, store, fixture};
}

describe('TransactionLedgerComponent type control', () => {
  it('sets type=credit when In is selected', () => {
    const {chip, navigate} = setup(null);
    chip('type-in').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {type: 'credit'},
      queryParamsHandling: 'merge',
    });
  });

  it('sets type=debit when Out is selected', () => {
    const {chip, navigate} = setup(null);
    chip('type-out').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {type: 'debit'},
      queryParamsHandling: 'merge',
    });
  });

  it('clears the type when All is selected', () => {
    const {chip, navigate} = setup('debit');
    chip('type-all').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {type: null},
      queryParamsHandling: 'merge',
    });
  });

  it('selects All for a type the store does not filter by', () => {
    const {selected} = setup(null);
    expect(selected('type-all')).toBe('true');
  });

  it('shows no duplicate removable Type chip for a deep-linked type', () => {
    const {root} = setup('credit');
    expect(root.textContent).not.toContain('Type:');
  });

  it('preselects the control from a deep-linked type', () => {
    const {selected} = setup('credit');
    expect(selected('type-in')).toBe('true');
    expect(selected('type-all')).toBe('false');
    expect(selected('type-out')).toBe('false');
  });
});

describe('TransactionLedgerComponent date range', () => {
  it('shows a clearable chip for the store date range and clears it through the URL', () => {
    const {root, navigate} = setup(null, {from: '2026-06-01', to: '2026-08-31'});
    const chip = root.querySelector('[data-testid="date-range-chip"]') as HTMLElement;
    expect(chip.textContent).toContain('2026-06-01 – 2026-08-31');
    chip.querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {from: null, to: null},
      queryParamsHandling: 'merge',
    });
  });

  it('shows no chip while the store holds no date range', () => {
    const {root} = setup(null);
    expect(root.querySelector('[data-testid="date-range-chip"]')).toBeNull();
  });
});

describe('TransactionLedgerComponent account filter', () => {
  it('selects an account through the URL, pushing a history entry', () => {
    const {navigate, store, fixture, root} = setup(null);
    store.accounts.set([
      {accountId: 'a1', label: 'Monzo'},
      {accountId: 'a2', label: 'AIB'},
    ] as never);
    fixture.detectChanges();

    const aib = Array.from(root.querySelectorAll<HTMLElement>('cmn-chip button')).find(b =>
      b.textContent?.includes('AIB')
    );
    aib?.click();

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {account: 'a2'},
      queryParamsHandling: 'merge',
    });
  });
});

describe('TransactionLedgerComponent category', () => {
  it('shows a clearable chip for the store category and clears it through the URL', () => {
    const {root, navigate} = setup(null, {category: 'FAMILY_SUPPORT'});
    const chip = root.querySelector('[data-testid="category-chip"]') as HTMLElement;
    expect(chip.textContent).toContain('Category: Family Support');
    chip.querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {category: null},
      queryParamsHandling: 'merge',
    });
  });
});

describe('TransactionLedgerComponent search box', () => {
  const searchBox = (root: HTMLElement) =>
    root.querySelector('input[type="search"]') as HTMLInputElement;

  it('starts from the search the store holds', () => {
    const {root} = setup(null, {q: 'netflix'});
    expect(searchBox(root).value).toBe('netflix');
  });

  it('feeds typed text to the store', () => {
    const {root, store} = setup(null);
    const typed: string[] = [];
    const feed = store.applySearch.mock.calls[0][0] as {
      subscribe: (fn: (v: string) => void) => void;
    };
    feed.subscribe(value => typed.push(value));

    const input = searchBox(root);
    input.value = 'spotify';
    input.dispatchEvent(new Event('input'));

    expect(typed).toEqual(['spotify']);
  });

  it('never writes the URL itself', () => {
    const {root, navigate} = setup(null);
    const input = searchBox(root);
    input.value = 'spotify';
    input.dispatchEvent(new Event('input'));
    expect(navigate).not.toHaveBeenCalled();
  });

  it('restores the box when the store search changes (back/forward)', () => {
    const {root, store, fixture} = setup(null, {q: 'netflix'});
    store.search.set('spotify');
    fixture.detectChanges();
    expect(searchBox(root).value).toBe('spotify');
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
  it('shows skeleton rows and no ledger rows while loading', () => {
    const {store, fixture, root} = setup(null);
    store.isEmpty.set(false);
    store.isLoading.set(true);
    fixture.detectChanges();

    expect(root.querySelectorAll('cmn-skeleton').length).toBeGreaterThan(0);
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

  it('keeps the loaded rows under the error and through a retry', () => {
    const {store, fixture, root} = setup(null);
    store.transactions.set([ROW] as never);
    store.isEmpty.set(false);
    store.isLoading.set(true);
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
