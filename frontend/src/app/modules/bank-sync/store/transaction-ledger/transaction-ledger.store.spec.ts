import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, Router} from '@angular/router';
import {ErrorMessageService} from '@lifekit-hq/core';
import {of} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {TRANSACTION_FILTER_DEBOUNCE_MS} from '../../../../shared/constants/transaction-filters/transaction-filters.constants';
import {BankSyncService} from '../../services/bank-sync.service';
import {TransactionLedgerStore} from './transaction-ledger.store';

function createStore(query: Record<string, string>) {
  TestBed.configureTestingModule({
    providers: [
      TransactionLedgerStore,
      {provide: ActivatedRoute, useValue: {queryParamMap: of(convertToParamMap(query))}},
      {provide: Router, useValue: {navigate: vi.fn()}},
      {provide: ErrorMessageService, useValue: {resolve: () => null}},
      {
        provide: BankSyncService,
        useValue: {
          getAllTransactions: () =>
            of({items: [], totalCount: 0, offset: 0, limit: 50, hasMore: false}),
          getDashboardData: () => of(null),
          getAccounts: () => of(null),
        },
      },
    ],
  });
  return TestBed.inject(TransactionLedgerStore);
}

describe('TransactionLedgerStore', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('hydrates valid filters from the query string', () => {
    const store = createStore({
      type: 'credit',
      from: '2026-01-01',
      to: '2026-01-31',
      minAmountUsd: '10.5',
      maxAmountUsd: '99',
    });

    expect(store.filters()).toMatchObject({
      transactionType: 'credit',
      from: '2026-01-01',
      to: '2026-01-31',
      minAmount: 10.5,
      maxAmount: 99,
    });
  });

  it('ignores query values that do not parse', () => {
    const store = createStore({
      type: 'foo',
      from: 'garbage',
      to: '2026-1-1',
      minAmountUsd: 'abc',
      maxAmountUsd: 'NaN',
    });

    expect(store.filters()).toMatchObject({
      transactionType: null,
      from: null,
      to: null,
      minAmount: null,
      maxAmount: null,
    });
    expect(store.hasActiveFilter()).toBe(false);
  });

  it('keeps the typed amount text when the committed bound lands', () => {
    const store = createStore({});

    store.applyAmount({bound: 'minAmount', raw: '1.0'});
    vi.advanceTimersByTime(TRANSACTION_FILTER_DEBOUNCE_MS);
    TestBed.tick();

    expect(store.filters().minAmount).toBe(1);
    expect(store.inputText().minAmount).toBe('1.0');
  });

  it('keeps a pending amount when an unrelated filter changes', () => {
    const store = createStore({});

    store.applyAmount({bound: 'minAmount', raw: '50'});
    store.setFilters({transactionType: 'debit'});
    vi.advanceTimersByTime(TRANSACTION_FILTER_DEBOUNCE_MS);

    expect(store.filters().minAmount).toBe(50);
  });

  it('does not re-apply a pending amount after the filters are cleared', () => {
    const store = createStore({});
    store.setFilters({categories: ['FOOD_AND_DRINK']});

    store.applyAmount({bound: 'minAmount', raw: '50'});
    store.clearFilters();
    vi.advanceTimersByTime(TRANSACTION_FILTER_DEBOUNCE_MS);

    expect(store.filters().minAmount).toBeNull();
    expect(store.inputText().minAmount).toBe('');
  });
});
