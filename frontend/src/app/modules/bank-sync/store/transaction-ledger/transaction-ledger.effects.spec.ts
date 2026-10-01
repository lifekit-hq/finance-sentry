import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, Subject, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type AccountsResponse} from '../../models/bank-account/bank-account.model';
import {type DashboardData} from '../../models/dashboard/dashboard.model';
import {type GlobalTransactionDto} from '../../models/transaction/transaction.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {SEARCH_DEBOUNCE_MS, transactionLedgerEffects} from './transaction-ledger.effects';
import {PAGE_SIZE} from './transaction-ledger.state';

// Mirrors the private helper in effects.ts so test data stays in sync with the runtime.
function currentUtcMonthKey(): string {
  const now = new Date();
  return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, '0')}`;
}

function prevUtcMonthKey(): string {
  const now = new Date();
  const prev = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1));
  return `${prev.getUTCFullYear()}-${String(prev.getUTCMonth() + 1).padStart(2, '0')}`;
}

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

function buildDashboardData(currentOutflowUsd: number, priorOutflowUsd?: number): DashboardData {
  const monthlyFlow = [
    {
      month: currentUtcMonthKey(),
      currency: 'USD',
      inflow: 4800,
      outflow: currentOutflowUsd,
      net: 4800 - currentOutflowUsd,
      inflowUsd: 4800,
      outflowUsd: currentOutflowUsd,
      netUsd: 4800 - currentOutflowUsd,
    },
  ];
  if (priorOutflowUsd !== undefined) {
    monthlyFlow.push({
      month: prevUtcMonthKey(),
      currency: 'USD',
      inflow: 5000,
      outflow: priorOutflowUsd,
      net: 5000 - priorOutflowUsd,
      inflowUsd: 5000,
      outflowUsd: priorOutflowUsd,
      netUsd: 5000 - priorOutflowUsd,
    });
  }
  return {
    aggregatedBalance: {USD: 50_000},
    totalNetWorthUsd: 50_000,
    accountCount: 3,
    accountsByType: {banking: 2, brokerage: 1},
    monthlyFlow,
    topCategories: [],
    lastSyncTimestamp: null,
  };
}

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
// Pass initialOffset to simulate the state after nextPage() has advanced the cursor.
function buildStore(initialOffset = 0) {
  return {
    offset: signal(initialOffset),
    accountId: signal<string | null>(null),
    search: signal(''),
    setLoading: vi.fn(),
    setTransactions: vi.fn(),
    appendTransactions: vi.fn(),
    nextPage: vi.fn(),
    setError: vi.fn(),
    setMonthlyOutflowUsd: vi.fn(),
    setAccountId: vi.fn(),
    setSearch: vi.fn(),
    setAccounts: vi.fn(),
  };
}

function buildService() {
  return {
    getAllTransactions: vi.fn(),
    getDashboardData: vi.fn(),
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

      expect(store.setLoading).toHaveBeenCalled();
      expect(service.getAllTransactions).toHaveBeenCalledWith({offset: 0, limit: PAGE_SIZE});
      expect(store.setTransactions).toHaveBeenCalledWith([TX_ITEM], 1, false);
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

    it('surfaces a transactions error', () => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions.mockReturnValue(throwError(() => new Error('boom')));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).load());

      expect(store.setError).toHaveBeenCalled();
      expect(store.setTransactions).not.toHaveBeenCalled();
    });
  });

  describe('loadSummary', () => {
    it('sets current-month outflow and the account options', () => {
      const store = buildStore();
      const service = buildService();
      service.getDashboardData.mockReturnValue(of(buildDashboardData(2900)));
      service.getAccounts.mockReturnValue(of(ACCOUNTS));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadSummary());

      expect(store.setMonthlyOutflowUsd).toHaveBeenCalledWith(2900);
      expect(store.setAccounts).toHaveBeenCalledWith([
        {accountId: 'acc-1', label: 'Test Bank · 1234'},
      ]);
    });

    it('sums only the current-month outflowUsd and ignores past-month rows', () => {
      // Prior month has 3 000 outflowUsd - must NOT be included.
      const store = buildStore();
      const service = buildService();
      service.getDashboardData.mockReturnValue(of(buildDashboardData(2900, 3000)));
      service.getAccounts.mockReturnValue(of(ACCOUNTS));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadSummary());

      expect(store.setMonthlyOutflowUsd).toHaveBeenCalledWith(2900); // not 5 900
    });

    it('degrades to null outflow and no accounts when both reads fail', () => {
      const store = buildStore();
      const service = buildService();
      service.getDashboardData.mockReturnValue(throwError(() => new Error('network error')));
      service.getAccounts.mockReturnValue(throwError(() => new Error('network error')));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadSummary());

      expect(store.setMonthlyOutflowUsd).toHaveBeenCalledWith(null);
      expect(store.setAccounts).toHaveBeenCalledWith([]);
      expect(store.setError).not.toHaveBeenCalled();
    });

    it('sets outflow to 0 when monthlyFlow has no entry for the current month', () => {
      const store = buildStore();
      const service = buildService();
      service.getDashboardData.mockReturnValue(of({...buildDashboardData(2900), monthlyFlow: []}));
      service.getAccounts.mockReturnValue(of(ACCOUNTS));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadSummary());

      expect(store.setMonthlyOutflowUsd).toHaveBeenCalledWith(0);
    });
  });

  describe('loadMore', () => {
    it('calls nextPage, then fetches the next page using the updated offset', () => {
      // initialOffset=PAGE_SIZE simulates the state after nextPage() has advanced the cursor.
      // The real nextPage() mutates store state; here it is a vi.fn(), so we seed the offset.
      const store = buildStore(PAGE_SIZE);
      const service = buildService();
      const PAGE2 = {items: [], totalCount: 1, offset: PAGE_SIZE, limit: PAGE_SIZE, hasMore: false};
      service.getAllTransactions.mockReturnValue(of(PAGE2));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).loadMore());

      expect(store.nextPage).toHaveBeenCalled();
      expect(service.getAllTransactions).toHaveBeenCalledWith({
        offset: PAGE_SIZE,
        limit: PAGE_SIZE,
      });
      expect(store.appendTransactions).toHaveBeenCalledWith([], 1, false);
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
        effects.applyAccount('acc-1');
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

  describe('applyAccount', () => {
    it('stores a changed account and reloads from the first page', () => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).applyAccount('acc-1'));

      expect(store.setAccountId).toHaveBeenCalledWith('acc-1');
      expect(service.getAllTransactions).toHaveBeenCalledTimes(1);
    });

    it('ignores a value the store already holds', () => {
      const store = buildStore();
      const service = buildService();
      configure(service);

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).applyAccount(null));

      expect(store.setAccountId).not.toHaveBeenCalled();
      expect(service.getAllTransactions).not.toHaveBeenCalled();
    });
  });

  describe('applySearch', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('waits for typing to pause, then stores the term and reloads once', () => {
      const store = buildStore();
      const service = buildService();
      service.getAllTransactions.mockReturnValue(of(TX_RESPONSE));
      configure(service);
      const typed = new Subject<string>();

      TestBed.runInInjectionContext(() => transactionLedgerEffects(store).applySearch(typed));
      typed.next('co');
      typed.next('coffee');
      expect(service.getAllTransactions).not.toHaveBeenCalled();
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);

      expect(store.setSearch).toHaveBeenCalledTimes(1);
      expect(store.setSearch).toHaveBeenCalledWith('coffee');
      expect(service.getAllTransactions).toHaveBeenCalledTimes(1);
    });

    it('does not reload when the term is unchanged', () => {
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
