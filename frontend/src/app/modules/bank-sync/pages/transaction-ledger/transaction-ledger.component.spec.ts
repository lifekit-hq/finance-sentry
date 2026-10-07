import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, provideRouter, Router} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {BehaviorSubject} from 'rxjs';

import {SEARCH_DEBOUNCE_MS} from '../../store/transaction-ledger/transaction-ledger.effects';
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
  const params$ = new BehaviorSubject(convertToParamMap({...(type ? {type} : {}), ...extra}));
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
    applyAccount: () => undefined,
    applyType: vi.fn(),
    applyCategory: vi.fn(),
    applyDateRange: vi.fn(),
    applySearch: () => undefined,
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
      {provide: ActivatedRoute, useValue: {queryParamMap: params$}},
    ],
  });
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(TransactionLedgerComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const chip = (id: string) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement;
  const selected = (id: string) => chip(id).querySelector('button')?.getAttribute('aria-pressed');
  return {chip, selected, navigate, root, store, params$, fixture};
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

  it('drives the store type filter from the type query param', () => {
    const {store} = setup('credit');
    const followed = store.applyType.mock.calls[0][0] as () => unknown;
    expect(followed()).toBe('credit');
  });

  it('treats an unknown type query param as All', () => {
    const {store, selected} = setup('bogus');
    const followed = store.applyType.mock.calls[0][0] as () => unknown;
    expect(followed()).toBeNull();
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
  it('shows a clearable chip and drives the store from from/to query params', () => {
    const {root, store, navigate} = setup(null, {from: '2026-06-01', to: '2026-08-31'});
    const chip = root.querySelector('[data-testid="date-range-chip"]') as HTMLElement;
    expect(chip.textContent).toContain('2026-06-01 – 2026-08-31');
    expect(store.applyDateRange).toHaveBeenCalled();
    chip.querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {from: null, to: null},
      queryParamsHandling: 'merge',
    });
  });

  it('ignores malformed dates', () => {
    const {root} = setup(null, {from: 'garbage', to: '2026-8-1'});
    expect(root.querySelector('[data-testid="date-range-chip"]')).toBeNull();
  });
});

describe('TransactionLedgerComponent category', () => {
  it('shows a clearable chip and drives the server-side category from the query param', () => {
    const {root, store, navigate} = setup(null, {category: 'FAMILY_SUPPORT'});
    const chip = root.querySelector('[data-testid="category-chip"]') as HTMLElement;
    expect(chip.textContent).toContain('Category: Family Support');
    const followed = store.applyCategory.mock.calls[0][0] as () => unknown;
    expect(followed()).toBe('FAMILY_SUPPORT');
    chip.querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {category: null},
      queryParamsHandling: 'merge',
    });
  });
});

describe('TransactionLedgerComponent search param', () => {
  const searchBox = (root: HTMLElement) =>
    root.querySelector('input[type="search"]') as HTMLInputElement;

  afterEach(() => vi.useRealTimers());

  it('reads the initial search from q and feeds the store', () => {
    const {root, store} = setup(null, {q: 'netflix'});
    expect(searchBox(root).value).toBe('netflix');
    expect(store.applyDateRange).toHaveBeenCalled();
  });

  it('writes typed search to q, debounced, replacing history and keeping the other params', () => {
    vi.useFakeTimers();
    const {root, navigate} = setup(null, {account: 'a1', category: 'FOOD'});
    const input = searchBox(root);
    input.value = 'spotify';
    input.dispatchEvent(new Event('input'));
    expect(navigate).not.toHaveBeenCalled();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {q: 'spotify'},
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  });

  it('drops q from the URL when the box is cleared', () => {
    vi.useFakeTimers();
    const {root, navigate} = setup(null, {q: 'spotify'});
    const input = searchBox(root);
    input.value = '';
    input.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {q: null},
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  });

  it('restores the box from q on back/forward without writing the URL again', () => {
    vi.useFakeTimers();
    const {root, navigate, params$, fixture} = setup(null, {q: 'netflix'});
    params$.next(convertToParamMap({q: 'spotify', account: 'a1'}));
    fixture.detectChanges();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    expect(searchBox(root).value).toBe('spotify');
    expect(navigate).not.toHaveBeenCalled();
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
