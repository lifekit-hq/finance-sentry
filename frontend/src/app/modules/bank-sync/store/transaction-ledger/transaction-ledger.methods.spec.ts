import {TestBed} from '@angular/core/testing';
import {getState, signalStore, withMethods, withState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {EMPTY_TRANSACTION_FILTERS} from '../../../../shared/constants/transaction-filters/transaction-filters.constants';
import {transactionLedgerMethods} from './transaction-ledger.methods';
import {initialTransactionLedgerState, PAGE_SIZE} from './transaction-ledger.state';

const TestStore = signalStore(
  withState({...initialTransactionLedgerState, offset: PAGE_SIZE * 2}),
  withMethods(transactionLedgerMethods)
);

describe('transactionLedgerMethods', () => {
  it('setFilters merges the patch and resets the offset', () => {
    TestBed.configureTestingModule({providers: [TestStore]});
    const store = TestBed.inject(TestStore);

    store.setFilters({categories: ['TRAVEL']});

    const state = getState(store);
    expect(state.filters).toEqual({...EMPTY_TRANSACTION_FILTERS, categories: ['TRAVEL']});
    expect(state.offset).toBe(0);
  });

  it('resetFilters clears every filter and resets the offset', () => {
    TestBed.configureTestingModule({providers: [TestStore]});
    const store = TestBed.inject(TestStore);
    store.setFilters({search: 'rent', transactionType: 'debit'});

    store.resetFilters();

    expect(getState(store).filters).toEqual(EMPTY_TRANSACTION_FILTERS);
    expect(getState(store).offset).toBe(0);
  });

  it('startFirstPage drops the previous rows and paging', () => {
    TestBed.configureTestingModule({providers: [TestStore]});
    const store = TestBed.inject(TestStore);

    store.startFirstPage();

    const state = getState(store);
    expect(state.offset).toBe(0);
    expect(state.transactions).toEqual([]);
    expect(state.hasMore).toBe(false);
  });
});
