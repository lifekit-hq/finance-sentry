import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {RouterTestingHarness} from '@angular/router/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {patchState} from '@ngrx/signals';
import {unprotected} from '@ngrx/signals/testing';
import {NEVER} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {BankSyncService} from '../../services/bank-sync.service';
import {type TransactionLedgerState} from './transaction-ledger.state';
import {TransactionLedgerStore} from './transaction-ledger.store';

@Component({
  selector: 'fns-ledger-host',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [TransactionLedgerStore],
})
class HostComponent {
  public readonly store = inject(TransactionLedgerStore);
}

const service = {
  getAllTransactions: vi.fn(() => NEVER),
  getDashboardData: vi.fn(() => NEVER),
  getAccounts: vi.fn(() => NEVER),
};

async function open(url: string) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
      provideRouter([{path: 'transactions', component: HostComponent}]),
      {provide: BankSyncService, useValue: service},
    ],
  });
  const harness = await RouterTestingHarness.create();
  const host = await harness.navigateByUrl(url, HostComponent);
  await harness.fixture.whenStable();
  return {harness, store: host.store, router: TestBed.inject(Router)};
}

/** Flushes the store's URL-writing effect, then waits for the navigation it started. */
async function settle(fixture: {whenStable: () => Promise<unknown>}): Promise<void> {
  TestBed.tick();
  await fixture.whenStable();
}

const DATE_FROM = '2026-06-01';
const DATE_TO = '2026-08-31';

beforeEach(() => {
  service.getAllTransactions.mockClear();
});

describe('TransactionLedgerStore URL → state', () => {
  it('reads every filter param into state and loads that filter', async () => {
    const {store} = await open(
      `/transactions?account=acc-1&type=credit&category=FOOD&from=${DATE_FROM}&to=${DATE_TO}&q=netflix`
    );

    expect(store.accountId()).toBe('acc-1');
    expect(store.transactionType()).toBe('credit');
    expect(store.categories()).toEqual(['FOOD']);
    expect(store.from()).toBe(DATE_FROM);
    expect(store.to()).toBe(DATE_TO);
    expect(store.search()).toBe('netflix');
    expect(service.getAllTransactions).toHaveBeenCalledTimes(1);
    expect(service.getAllTransactions).toHaveBeenCalledWith({
      offset: 0,
      limit: 50,
      accountId: 'acc-1',
      transactionType: 'credit',
      category: ['FOOD'],
      from: DATE_FROM,
      to: DATE_TO,
      search: 'netflix',
    });
  });

  it('keeps every default when no param is present', async () => {
    const {store, router} = await open('/transactions');

    expect(store.accountId()).toBeNull();
    expect(store.transactionType()).toBeNull();
    expect(store.categories()).toEqual([]);
    expect(store.from()).toBeNull();
    expect(store.to()).toBeNull();
    expect(store.search()).toBe('');
    expect(router.url).toBe('/transactions');
    expect(service.getAllTransactions).toHaveBeenCalledWith({offset: 0, limit: 50});
  });

  it('falls back to the default for an unknown type', async () => {
    const {store} = await open('/transactions?type=bogus');

    expect(store.transactionType()).toBeNull();
  });

  it.each(['garbage', '2026-8-1', '2026-08-01T00:00:00'])(
    'reads a malformed date %s as an open bound',
    async date => {
      const {store} = await open(`/transactions?from=${date}&to=${date}`);

      expect(store.from()).toBeNull();
      expect(store.to()).toBeNull();
    }
  );

  it('reads an empty account or category as absent', async () => {
    const {store} = await open('/transactions?account=&category=');

    expect(store.accountId()).toBeNull();
    expect(store.categories()).toEqual([]);
  });

  it('follows the URL when it changes after load and reloads once', async () => {
    const {harness, store} = await open('/transactions?account=acc-1&q=netflix');
    service.getAllTransactions.mockClear();

    await harness.navigateByUrl('/transactions?type=debit');
    await settle(harness.fixture);

    expect(store.accountId()).toBeNull();
    expect(store.search()).toBe('');
    expect(store.transactionType()).toBe('debit');
    expect(service.getAllTransactions).toHaveBeenCalledTimes(1);
  });
});

describe('TransactionLedgerStore state → URL', () => {
  type Change = Partial<
    Pick<TransactionLedgerState, 'accountId' | 'transactionType' | 'categories' | 'from' | 'to'>
  >;

  it.each<[string, Change, string]>([
    ['account', {accountId: 'acc-1'}, '/transactions?account=acc-1'],
    ['type', {transactionType: 'debit'}, '/transactions?type=debit'],
    ['category', {categories: ['FOOD']}, '/transactions?category=FOOD'],
    ['from', {from: DATE_FROM}, `/transactions?from=${DATE_FROM}`],
    ['to', {to: DATE_TO}, `/transactions?to=${DATE_TO}`],
  ])('writes a changed %s without adding a history entry', async (_, change, expected) => {
    const {harness, store, router} = await open('/transactions');
    const entries = history.length;

    patchState(unprotected(store), change);
    await settle(harness.fixture);

    expect(router.url).toBe(expected);
    expect(history.length).toBe(entries);
  });

  it('writes the search, keeps the other params and does not add a history entry', async () => {
    const {harness, store, router} = await open('/transactions?account=acc-1');
    const entries = history.length;

    store.setSearch('spotify');
    await settle(harness.fixture);

    expect(router.url).toBe('/transactions?account=acc-1&q=spotify');
    expect(history.length).toBe(entries);
  });

  it('drops a param again when it returns to its default', async () => {
    const {harness, store, router} = await open(
      `/transactions?type=debit&from=${DATE_FROM}&q=spotify`
    );

    patchState(unprotected(store), {transactionType: null, from: null});
    store.setSearch('');
    await settle(harness.fixture);

    expect(router.url).toBe('/transactions');
  });

  it('treats a blank search as no search', async () => {
    const {harness, store, router} = await open('/transactions?q=spotify');

    store.setSearch('   ');
    await settle(harness.fixture);

    expect(store.search()).toBe('');
    expect(router.url).toBe('/transactions');
  });
});
