import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';

import {EMPTY_TRANSACTION_FILTERS} from '../../../../shared/constants/transaction-filters/transaction-filters.constants';
import {type TransactionFilters} from '../../../../shared/models/transaction-filters/transaction-filters.model';
import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {TransactionLedgerComponent} from './transaction-ledger.component';

function setup(filters: Partial<TransactionFilters> = {}, overrides: {hasMore?: boolean} = {}) {
  const store = {
    filters: signal<TransactionFilters>({...EMPTY_TRANSACTION_FILTERS, ...filters}),
    accountOptions: signal([]),
    dateRange: signal({from: null, to: null}),
    dayGroups: signal([]),
    monthlyOutflow: signal(null),
    topCategory: signal(null),
    errorMessage: signal(''),
    isLoading: signal(false),
    isEmpty: signal(true),
    hasActiveFilter: signal(Object.keys(filters).length > 0),
    hasMore: signal(overrides.hasMore ?? false),
    transactions: signal([]),
    totalCount: signal(0),
    setFilters: vi.fn(),
    inputText: signal({minAmount: '', maxAmount: '', search: ''}),
    clearFilters: vi.fn(),
    applySearch: vi.fn(),
    applyAmount: vi.fn(),
    load: vi.fn(),
    loadMore: vi.fn(),
  };
  TestBed.overrideComponent(TransactionLedgerComponent, {
    set: {providers: [{provide: TransactionLedgerStore, useValue: store}]},
  });
  TestBed.configureTestingModule({
    providers: [{provide: CategoryStore, useValue: {filterOptions: signal([])}}],
  });
  const fixture = TestBed.createComponent(TransactionLedgerComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const byTestId = (id: string) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement;
  const selected = (id: string) =>
    byTestId(id).querySelector('button')?.getAttribute('aria-pressed');
  return {byTestId, selected, root, store};
}

describe('TransactionLedgerComponent type control', () => {
  it('sets the credit type when In is selected', () => {
    const {byTestId, store} = setup();
    byTestId('type-in').querySelector('button')?.click();
    expect(store.setFilters).toHaveBeenCalledWith({transactionType: 'credit'});
  });

  it('sets the debit type when Out is selected', () => {
    const {byTestId, store} = setup();
    byTestId('type-out').querySelector('button')?.click();
    expect(store.setFilters).toHaveBeenCalledWith({transactionType: 'debit'});
  });

  it('clears the type when All is selected', () => {
    const {byTestId, store} = setup({transactionType: 'debit'});
    byTestId('type-all').querySelector('button')?.click();
    expect(store.setFilters).toHaveBeenCalledWith({transactionType: null});
  });

  it('preselects the control from the store filters', () => {
    const {selected} = setup({transactionType: 'credit'});
    expect(selected('type-in')).toBe('true');
    expect(selected('type-all')).toBe('false');
    expect(selected('type-out')).toBe('false');
  });
});

describe('TransactionLedgerComponent empty state', () => {
  it('renders the filtered empty state and no Load More when nothing matches', () => {
    const {root, byTestId} = setup({categories: ['TRAVEL']}, {hasMore: true});
    expect(byTestId('ledger-empty')?.textContent).toContain('No matching transactions');
    expect(root.textContent).not.toContain('Load More');
    expect(root.querySelector('[data-testid="ledger-row"]')).toBeNull();
  });

  it('offers to clear active filters', () => {
    const {byTestId, store} = setup({search: 'rent'});
    byTestId('clear-filters').querySelector('button')?.click();
    expect(store.clearFilters).toHaveBeenCalled();
  });
});
