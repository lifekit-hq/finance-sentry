import {computed, signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {of, Subject, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type AccountsResponse} from '../../models/bank-account/bank-account.model';
import {type SpendingSummary} from '../../models/dashboard/spending-summary.model';
import {
  type GlobalTransactionDto,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {type LedgerFilter} from './transaction-ledger.computed';
import {SEARCH_DEBOUNCE_MS, transactionLedgerEffects} from './transaction-ledger.effects';
import {type OutflowScope, PAGE_SIZE} from './transaction-ledger.state';

const NO_FILTER: LedgerFilter = {
  accountId: null,
  transactionType: null,
  categories: [],
  from: null,
  to: null,
  search: '',
};

const TX_ITEM: GlobalTransactionDto = {
  transactionId: 'tx-1',
  accountId: 'acc-1',
  bankName: 'Test Bank',
  currency: 'USD',
  amount: 400,
  amountUsd: 400,
  date: '2026-08-10',
  postedDate: '2026-08-10',
  description: 'Grocery',
  transactionType: 'debit',
  merchantCategory: null,
  isPending: false,
  createdAt: '2026-08-10T00:00:00Z',
};

const TX_RESPONSE = {
  items: [TX_ITEM],
  totalCount: 1,
  offset: 0,
  limit: PAGE_SIZE,
  hasMore: false,
};

const ACCOUNTS: AccountsResponse = {
  accounts: [
    {
      accountId: 'acc-1',
      bankName: 'Test Bank',
      accountType: 'checking',
      accountNumberLast4: '1234',
      currency: 'USD',
      ownerName: 'Owner',
      currentBalance: 10,
      availableBalance: 10,
      syncStatus: 'active',
      lastSyncTimestamp: null,
      lastSyncDurationMs: null,
      provider: 'monobank',
      createdAt: '2026-08-10T00:00:00Z',
    },
  ],
  totalCount: 1,
  // eslint-disable-next-line @typescript-eslint/naming-convention -- mirrors the API payload
  currency_totals: {USD: 10},
};

// Signals are real so the EffectsStore type constraint is satisfied.
// Pass initialOffset to simulate the offset of the last page already on screen.
function buildStore(initialOffset = 0, transactions: GlobalTransactionDto[] = []) {
  const accountId = signal<string | null>(null);
  const transactionType = signal<TransactionType | null>(null);
  const categories = signal<string[]>([]);
  const from = signal<string | null>(null);
  const to = signal<string | null>(null);
  const search = signal('');
  return {
    transactions: signal(transactions),
    offset: signal(initialOffset),
    status: signal<AsyncStatus>('idle'),
    accountId,
    transactionType,
    categories,
    from,
    to,
    search,
    filter: computed<LedgerFilter>(() => ({
      accountId: accountId(),
      transactionType: transactionType(),
      categories: categories(),
      from: from(),
      to: to(),
      search: search().trim(),
    })),
    rowsFilter: signal<LedgerFilter | null>(NO_FILTER),
    setRevalidating: vi.fn(),
    clearRows: vi.fn(),
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    setTransactions: vi.fn(),
    appendTransactions: vi.fn(),
    nextPage: vi.fn(),
    setError: vi.fn(),
    setAppendLoading: vi.fn(),
    setAppendSuccess: vi.fn(),
    setAppendError: vi.fn(),
    setAppendIdle: vi.fn(),
    setSpending: vi.fn(),
    setSearch: vi.fn(),
    setAccounts: vi.fn(),
  };
}

function buildService() {
  return {
    getAllTransactions: vi.fn(),
    getSpendingSummary: vi.fn(),
    getAccounts: vi.fn(),
  };
}

function configure(service: ReturnType<typeof buildService>): void {
  TestBed.configureTestingModule({
    providers: [{provide: BankSyncService, useValue: service}],
  });
}

describe('transactionLedgerEffects', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  describe('load', () => {
    it('fetches the first page and sets transactions', () => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      // The rows on screen stay (kept, dimmed) until the first page replaces them.
      expect(store.clearRows).not.toHaveBeenCalled();
      expect(store.setLoading).toHaveBeenCalled();
      expect(service.getAllTransactions).toHaveBeenCalledWith({offset: 0, limit: PAGE_SIZE});
      expect(store.setTransactions).toHaveBeenCalledWith([TX_ITEM], 1, false, NO_FILTER);
      expect(store.setSuccess).toHaveBeenCalled();
    });

    it('sends the active account and a trimmed search to the server', () => {
      const store = buildStore();
      store.accountId.set('acc-1');
      store.search.set('  coffee ');
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(service.getAllTransactions).toHaveBeenCalledWith({
        offset: 0,
        limit: PAGE_SIZE,
        accountId: 'acc-1',
        search: 'coffee',
      });
    });

    it('sends the active type filter to the server', () => {
      const store = buildStore();
      store.transactionType.set('credit');
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(service.getAllTransactions).toHaveBeenCalledWith({
        offset: 0,
        limit: PAGE_SIZE,
        transactionType: 'credit',
      });
    });

    it('sends the active category to the server', () => {
      const store = buildStore();
      store.categories.set(['FAMILY_SUPPORT']);
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(service.getAllTransactions).toHaveBeenCalledWith({
        offset: 0,
        limit: PAGE_SIZE,
        category: ['FAMILY_SUPPORT'],
      });
    });

    it('records the filter the landed rows were requested for', () => {
      const store = buildStore();
      store.accountId.set('acc-1');
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(store.setTransactions).toHaveBeenCalledWith([TX_ITEM], 1, false, {
        ...NO_FILTER,
        accountId: 'acc-1',
      });
    });

    it('keeps the rows on screen when revalidating them fails', () => {
      const store = buildStore(0, [TX_ITEM]);
      const service = buildService();
      service.getAllTransactions.mockReturnValue(throwError(() => new Error('boom')));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(store.setError).toHaveBeenCalled();
      expect(store.clearRows).not.toHaveBeenCalled();
      expect(store.setRevalidating).not.toHaveBeenCalled();
      expect(store.setTransactions).not.toHaveBeenCalled();
      expect(store.setSuccess).not.toHaveBeenCalled();
    });

    it('drops the previous query rows when the load for a new filter fails', () => {
      const store = buildStore(0, [TX_ITEM]);
      store.accountId.set('acc-2');
      const service = buildService();
      service.getAllTransactions.mockReturnValue(throwError(() => new Error('boom')));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(store.setRevalidating).toHaveBeenCalledWith(false);
      expect(store.clearRows).toHaveBeenCalled();
      expect(store.setError).toHaveBeenCalled();
      expect(store.setTransactions).not.toHaveBeenCalled();
    });

    it('drops the rows when none have landed yet and the first page fails', () => {
      const store = buildStore();
      store.rowsFilter.set(null);
      const service = buildService();
      service.getAllTransactions.mockReturnValue(throwError(() => new Error('boom')));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(store.clearRows).toHaveBeenCalled();
      expect(store.setError).toHaveBeenCalled();
    });
  });

  describe('loadAccounts', () => {
    it('offers the user accounts as chips', () => {
      const store = buildStore();
      const service = buildService();
      service.getAccounts.mockReturnValue(of(ACCOUNTS));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadAccounts());

      expect(store.setAccounts).toHaveBeenCalledWith([
        {accountId: 'acc-1', label: 'Test Bank · 1234'},
      ]);
    });

    it('degrades to no accounts when the read fails', () => {
      const store = buildStore();
      const service = buildService();
      service.getAccounts.mockReturnValue(throwError(() => new Error('network error')));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadAccounts());

      expect(store.setAccounts).toHaveBeenCalledWith([]);
      expect(store.setError).not.toHaveBeenCalled();
    });
  });

  describe('watchSpendingScope', () => {
    const MAY = {from: '2026-05-01', to: '2026-05-31'};
    const SUMMARY: SpendingSummary = {outflow: 1000, currency: 'EUR', topCategory: 'GROCERIES'};

    function run(
      scope: ReturnType<typeof signal<OutflowScope>>,
      service: ReturnType<typeof buildService>
    ) {
      const store = buildStore();
      configure(service);
      TestBed.runInInjectionContext(() =>
        transactionLedgerEffects(store).watchSpendingScope(scope)
      );
      TestBed.tick();
      return store;
    }

    it('asks the server for the account and dates and stores the answer with its scope', () => {
      const service = buildService();
      service.getSpendingSummary.mockReturnValue(of(SUMMARY));
      const scope = signal({accountId: 'acc-1', ...MAY});

      const store = run(scope, service);

      expect(service.getSpendingSummary).toHaveBeenCalledWith({accountId: 'acc-1', ...MAY});
      expect(store.setSpending).toHaveBeenCalledWith({accountId: 'acc-1', ...MAY, ...SUMMARY});
    });

    it('asks for every scope, all accounts and open dates included, and reads no rows', () => {
      const service = buildService();
      service.getSpendingSummary.mockReturnValue(of(SUMMARY));
      const scope = signal({accountId: null, from: null, to: null});

      run(scope, service);

      expect(service.getSpendingSummary).toHaveBeenCalledWith({
        accountId: null,
        from: null,
        to: null,
      });
      expect(service.getAllTransactions).not.toHaveBeenCalled();
    });

    it('asks again when the account or the dates change, not when they stay', () => {
      const service = buildService();
      service.getSpendingSummary.mockReturnValue(of(SUMMARY));
      const scope = signal({accountId: 'acc-1', ...MAY});
      run(scope, service);

      scope.set({accountId: 'acc-1', ...MAY});
      TestBed.tick();
      expect(service.getSpendingSummary).toHaveBeenCalledTimes(1);

      scope.set({accountId: 'acc-2', ...MAY});
      TestBed.tick();
      expect(service.getSpendingSummary).toHaveBeenCalledTimes(2);
    });

    it('leaves the figure unknown when the read fails', () => {
      const service = buildService();
      service.getSpendingSummary.mockReturnValue(throwError(() => new Error('boom')));
      const scope = signal({accountId: 'acc-1', ...MAY});

      const store = run(scope, service);

      expect(store.setSpending).not.toHaveBeenCalled();
    });
  });

  describe('loadMore', () => {
    it('waits while the first page is loading', () => {
      const store = buildStore(PAGE_SIZE);
      store.status.set('loading');
      const service = buildService();
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadMore());

      expect(service.getAllTransactions).not.toHaveBeenCalled();
      expect(store.setAppendLoading).not.toHaveBeenCalled();
    });

    it('refuses while the filter changed and its first page has not started', () => {
      const store = buildStore(PAGE_SIZE, [TX_ITEM]);
      store.accountId.set('acc-2');
      const service = buildService();
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadMore());

      expect(service.getAllTransactions).not.toHaveBeenCalled();
      expect(store.setAppendLoading).not.toHaveBeenCalled();
    });

    it('refuses while no rows have landed', () => {
      const store = buildStore(PAGE_SIZE);
      store.rowsFilter.set(null);
      const service = buildService();
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadMore());

      expect(service.getAllTransactions).not.toHaveBeenCalled();
    });

    it('fetches the page after the loaded one, then advances the offset and appends', () => {
      const store = buildStore(PAGE_SIZE);
      const service = buildService();
      const PAGE3 = {
        items: [],
        totalCount: 1,
        offset: 2 * PAGE_SIZE,
        limit: PAGE_SIZE,
        hasMore: false,
      };
      service.getAllTransactions.mockReturnValue(of(PAGE3));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadMore());

      expect(service.getAllTransactions).toHaveBeenCalledWith({
        offset: 2 * PAGE_SIZE,
        limit: PAGE_SIZE,
      });
      expect(store.setAppendLoading).toHaveBeenCalled();
      expect(store.nextPage).toHaveBeenCalled();
      expect(store.appendTransactions).toHaveBeenCalledWith([], 1, false);
      expect(store.setAppendSuccess).toHaveBeenCalled();
      expect(store.setLoading).not.toHaveBeenCalled();
    });

    it('keeps the offset on a failed next page so Try again repeats the same request', () => {
      const store = buildStore(0, [TX_ITEM]);
      store.nextPage.mockImplementation(() => store.offset.update(o => o + PAGE_SIZE));
      const service = buildService();
      service.getAllTransactions
        .mockReturnValueOnce(throwError(() => new Error('boom')))
        .mockReturnValueOnce(of({...TX_RESPONSE, offset: PAGE_SIZE}));
      configure(service);

      TestBed.runInInjectionContext(() => {
        const effects = transactionLedgerEffects(store);
        effects.loadMore();
        effects.loadMore();
      });

      expect(service.getAllTransactions).toHaveBeenNthCalledWith(1, {
        offset: PAGE_SIZE,
        limit: PAGE_SIZE,
      });
      expect(service.getAllTransactions).toHaveBeenNthCalledWith(2, {
        offset: PAGE_SIZE,
        limit: PAGE_SIZE,
      });
      expect(store.setAppendError).toHaveBeenCalledTimes(1);
      expect(store.setError).not.toHaveBeenCalled();
      expect(store.appendTransactions).toHaveBeenCalledTimes(1);
      expect(store.offset()).toBe(PAGE_SIZE);
    });

    it('drops an in-flight next page when a filter change reloads the first page', () => {
      const store = buildStore(PAGE_SIZE);
      const service = buildService();
      const stalePage = new Subject<unknown>();
      service.getAllTransactions
        .mockReturnValueOnce(stalePage)
        .mockReturnValueOnce(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => {
        const effects = transactionLedgerEffects(store);
        effects.loadMore();
        effects.watchFilter(of({...NO_FILTER, accountId: 'acc-1'}));
      });
      stalePage.next({
        items: [TX_ITEM],
        totalCount: 99,
        offset: PAGE_SIZE,
        limit: PAGE_SIZE,
        hasMore: true,
      });

      expect(store.appendTransactions).not.toHaveBeenCalled();
      expect(store.setTransactions).toHaveBeenCalledTimes(1);
    });
  });

  describe('retry', () => {
    it('re-fetches a failed first page and replaces the rows', () => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions
        .mockReturnValueOnce(throwError(() => new Error('boom')))
        .mockReturnValueOnce(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => {
        const effects = transactionLedgerEffects(store);
        effects.load();
        effects.retry();
      });

      expect(service.getAllTransactions).toHaveBeenLastCalledWith({offset: 0, limit: PAGE_SIZE});
      expect(store.setTransactions).toHaveBeenCalledWith([TX_ITEM], 1, false, NO_FILTER);
      expect(store.appendTransactions).not.toHaveBeenCalled();
    });
  });

  describe('watchFilter', () => {
    it('loads the first page for the filter it starts with', () => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);
      const filter = signal(NO_FILTER);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).watchFilter(filter));
      TestBed.tick();

      expect(service.getAllTransactions).toHaveBeenCalledTimes(1);
      expect(store.setTransactions).toHaveBeenCalledWith([TX_ITEM], 1, false, NO_FILTER);
    });

    it.each<[string, Partial<LedgerFilter>]>([
      ['account', {accountId: 'acc-1'}],
      ['type', {transactionType: 'debit'}],
      ['category', {categories: ['FOOD_AND_DRINK']}],
      ['from bound', {from: '2026-06-01'}],
      ['to bound', {to: '2026-08-31'}],
      ['search', {search: 'coffee'}],
    ])('reloads from the first page when the %s changes', (_, change) => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);
      const filter = signal(NO_FILTER);
      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).watchFilter(filter));
      TestBed.tick();

      filter.set({...NO_FILTER, ...change});
      TestBed.tick();

      expect(service.getAllTransactions).toHaveBeenCalledTimes(2);
    });

    it('ignores a filter that carries the same values', () => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);
      const filter = signal(NO_FILTER);
      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).watchFilter(filter));
      TestBed.tick();

      filter.set({...NO_FILTER});
      TestBed.tick();

      expect(service.getAllTransactions).toHaveBeenCalledTimes(1);
    });
  });

  describe('applySearch', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('waits for typing to pause, then stores the term once', () => {
      const store = buildStore();
      configure(buildService());
      const typed = new Subject<string>();

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).applySearch(typed));
      typed.next('co');
      typed.next('coffee');
      expect(store.setSearch).not.toHaveBeenCalled();
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);

      expect(store.setSearch).toHaveBeenCalledTimes(1);
      expect(store.setSearch).toHaveBeenCalledWith('coffee');
    });

    it('does not store a term that only differs by whitespace', () => {
      const store = buildStore();
      const service = buildService();
      configure(service);
      const typed = new Subject<string>();

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).applySearch(typed));
      typed.next('  ');
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);

      expect(store.setSearch).not.toHaveBeenCalled();
    });
  });
});
