import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, provideRouter, Router} from '@angular/router';
import {CmnDrawerService} from '@lifekit-hq/ui';
import {BehaviorSubject, Subject} from 'rxjs';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {type TransactionFilterSelection} from '../../models/transaction/transaction-filter.model';
import {SEARCH_DEBOUNCE_MS} from '../../store/transaction-ledger/transaction-ledger.effects';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';
import {TransactionLedgerComponent} from './transaction-ledger.component';

function setup(type: string | null, extra: Record<string, string | string[]> = {}) {
  const params$ = new BehaviorSubject(convertToParamMap({...(type ? {type} : {}), ...extra}));
  const closed$ = new Subject<TransactionFilterSelection | undefined>();
  const drawerOpen = vi.fn(() => ({afterClosed: () => closed$}));
  const store = {
    accounts: signal([]),
    monthlyOutflow: signal(null),
    monthlyOutflowCurrency: signal('USD'),
    topCategory: signal(null),
    errorMessage: signal<string | null>(null),
    load: vi.fn(),
    isLoading: signal(false),
    isEmpty: signal(true),
    hasActiveFilter: signal(false),
    hasMore: signal(false),
    transactions: signal([]),
    applyAccount: () => undefined,
    applyType: vi.fn(),
    applyCategories: vi.fn(),
    applyDateRange: vi.fn(),
    applySearch: () => undefined,
  };
  TestBed.overrideComponent(TransactionLedgerComponent, {
    set: {providers: [{provide: TransactionLedgerStore, useValue: store}]},
  });
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: ActivatedRoute, useValue: {queryParamMap: params$}},
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
  return {chip, navigate, root, store, params$, fixture, drawerOpen, closed$};
}

describe('TransactionLedgerComponent filter sheet', () => {
  it('opens the responsive Filters sheet seeded with the filters in the URL', () => {
    const {chip, drawerOpen} = setup('credit', {
      from: '2026-06-01',
      to: '2026-08-31',
      category: 'FOOD',
    });
    chip('filter-button').querySelector('button')?.click();
    expect(drawerOpen).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        title: 'Filters',
        mode: 'responsive',
        data: {from: '2026-06-01', to: '2026-08-31', type: 'credit', categories: ['FOOD']},
      })
    );
  });

  it('writes the applied Period + Type + Category to the URL and keeps account and search', () => {
    const {chip, navigate, closed$} = setup(null, {account: 'a1', q: 'x'});
    chip('filter-button').querySelector('button')?.click();
    closed$.next({
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

  it('changes nothing when the sheet is dismissed', () => {
    const {chip, navigate, closed$} = setup(null);
    chip('filter-button').querySelector('button')?.click();
    closed$.next(undefined);
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe('TransactionLedgerComponent period chips', () => {
  it('writes the picked period dates to the URL and keeps the other params', () => {
    const {chip, navigate} = setup('debit', {account: 'a1'});
    chip('period-last-month').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: LedgerPeriodUtils.dates('last-month'),
      queryParamsHandling: 'merge',
    });
  });

  it('selects the chip whose dates are in the URL', () => {
    const {chip} = setup(null, {...LedgerPeriodUtils.dates('3m')});
    expect(chip('period-3m').querySelector('button')?.getAttribute('aria-pressed')).toBe('true');
    expect(chip('period-this-month').querySelector('button')?.getAttribute('aria-pressed')).toBe(
      'false'
    );
  });

  it('selects no period chip for a custom range', () => {
    const {root} = setup(null, {from: '2020-01-01', to: '2020-01-31'});
    expect(
      root.querySelectorAll('[role="group"][aria-label="Period filter"] [aria-pressed="true"]')
    ).toHaveLength(0);
  });
});

describe('TransactionLedgerComponent applied filter chips', () => {
  it('shows no chip row without filters', () => {
    const {root} = setup(null);
    expect(root.querySelector('[data-testid="applied-filters"]')).toBeNull();
  });

  it('shows a chip per period, type and category, dismissible one by one', () => {
    const {chip, navigate} = setup('credit', {
      from: '2026-06-01',
      to: '2026-08-31',
      category: ['FAMILY_SUPPORT', 'FOOD'],
    });
    expect(chip('chip-date-range').getAttribute('label')).toBe('Dates: 2026-06-01 – 2026-08-31');
    expect(chip('chip-type').getAttribute('label')).toBe('Type: In');
    expect(chip('chip-category-FAMILY_SUPPORT').getAttribute('label')).toBe(
      'Category: Family Support'
    );

    chip('chip-category-FOOD').dispatchEvent(new CustomEvent('lk-dismissible-chip-remove'));
    expect(navigate).toHaveBeenLastCalledWith([], {
      queryParams: {
        from: '2026-06-01',
        to: '2026-08-31',
        type: 'credit',
        category: ['FAMILY_SUPPORT'],
      },
      queryParamsHandling: 'merge',
    });

    chip('chip-type').dispatchEvent(new CustomEvent('lk-dismissible-chip-remove'));
    expect(navigate).toHaveBeenLastCalledWith([], {
      queryParams: expect.objectContaining({type: null}),
      queryParamsHandling: 'merge',
    });

    chip('chip-date-range').dispatchEvent(new CustomEvent('lk-dismissible-chip-remove'));
    expect(navigate).toHaveBeenLastCalledWith([], {
      queryParams: expect.objectContaining({from: null, to: null}),
      queryParamsHandling: 'merge',
    });
  });

  it('names a quick period by its label', () => {
    const {from, to} = LedgerPeriodUtils.dates('last-month');
    const {chip} = setup(null, {from, to});
    expect(chip('chip-date-range').getAttribute('label')).toBe('Period: Last month');
  });

  it('Clear drops every sheet filter and leaves account and search alone', () => {
    const {chip, navigate} = setup('debit', {account: 'a1', category: 'FOOD', from: '2026-06-01'});
    chip('filter-clear').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {from: null, to: null, type: null, category: null},
      queryParamsHandling: 'merge',
    });
  });

  it('counts the active filters on the filter button badge', () => {
    const {root} = setup('debit', {category: ['FOOD', 'FAMILY_SUPPORT'], from: '2026-06-01'});
    expect(root.querySelector('cmn-badge')?.textContent).toContain('4');
  });

  it('drives the store from the type, category and date params', () => {
    const {store} = setup('credit', {category: ['FOOD', 'FAMILY_SUPPORT']});
    expect((store.applyType.mock.calls[0][0] as () => unknown)()).toBe('credit');
    expect((store.applyCategories.mock.calls[0][0] as () => unknown)()).toEqual([
      'FOOD',
      'FAMILY_SUPPORT',
    ]);
  });

  it('treats an unknown type and malformed dates as no filter', () => {
    const {root, store} = setup('bogus', {from: 'garbage', to: '2026-8-1'});
    expect((store.applyType.mock.calls[0][0] as () => unknown)()).toBeNull();
    expect(root.querySelector('[data-testid="applied-filters"]')).toBeNull();
  });
});

describe('TransactionLedgerComponent search param', () => {
  const searchBox = (root: HTMLElement) =>
    root.querySelector('input[type="search"], input') as HTMLInputElement;

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
    expect(store.load).toHaveBeenCalledOnce();
  });
});
