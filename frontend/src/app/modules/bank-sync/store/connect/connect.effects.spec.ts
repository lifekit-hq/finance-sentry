import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type AccountsResponse,
  type BankAccount,
} from '../../models/bank-account/bank-account.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {connectEffects} from './connect.effects';

function buildStore() {
  return {
    setSyncing: vi.fn(),
    setPolling: vi.fn(),
    setSuccess: vi.fn(),
    setError: vi.fn(),
    setInstitutionType: vi.fn(),
    selectProvider: vi.fn(),
    connectStatus: vi.fn().mockReturnValue('idle' as const),
    institutionType: vi.fn().mockReturnValue(null),
  };
}

function buildBankSync() {
  return {
    getAccounts: vi.fn(),
  };
}

const ACTIVE_RESPONSE: AccountsResponse = {
  accounts: [{accountId: 'a1', syncStatus: 'active'} as unknown as BankAccount],
  totalCount: 1,
  // eslint-disable-next-line @typescript-eslint/naming-convention
  currency_totals: {},
};

function configure(bankSync: ReturnType<typeof buildBankSync>): void {
  TestBed.configureTestingModule({
    providers: [
      {provide: BankSyncService, useValue: bankSync},
      {provide: Router, useValue: {navigateByUrl: vi.fn()} as unknown as Router},
    ],
  });
}

function monobankStrategy(
  submitImpl: () => ReturnType<ConnectStrategy['submit']>
): ConnectStrategy {
  return {
    slug: 'monobank',
    formComponent: class {} as unknown as ConnectStrategy['formComponent'],
    submit: vi.fn().mockImplementation(submitImpl),
  };
}

describe('connectEffects.connect', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('marks syncing, calls strategy.submit, and triggers polling for bank outcomes', () => {
    const store = buildStore();
    const bankSync = buildBankSync();
    bankSync.getAccounts.mockReturnValue(of(ACTIVE_RESPONSE));
    const strategy = monobankStrategy(() =>
      of({successCode: 'POLLING', count: 1, institutionType: 'bank'})
    );
    configure(bankSync);

    TestBed.runInInjectionContext(() => {
      connectEffects(store).connect({strategy, payload: undefined});
    });

    expect(store.selectProvider).toHaveBeenCalledWith('monobank');
    expect(store.setInstitutionType).toHaveBeenCalledWith('bank');
    expect(store.setSyncing).toHaveBeenCalled();
    // eslint-disable-next-line @typescript-eslint/unbound-method
    expect(strategy.submit).toHaveBeenCalledWith(undefined);
    expect(store.setPolling).toHaveBeenCalled();
  });

  it('non-bank outcome marks success directly without polling', () => {
    const store = buildStore();
    const bankSync = buildBankSync();
    const strategy: ConnectStrategy = {
      slug: 'binance',
      formComponent: class {} as unknown as ConnectStrategy['formComponent'],
      submit: vi
        .fn()
        .mockReturnValue(of({successCode: 'POLLING', count: 1, institutionType: 'crypto'})),
    };
    configure(bankSync);

    TestBed.runInInjectionContext(() => {
      connectEffects(store).connect({strategy, payload: {apiKey: 'k', apiSecret: 's'}});
    });

    expect(store.selectProvider).toHaveBeenCalledWith('binance');
    expect(store.setSuccess).toHaveBeenCalled();
    expect(store.setPolling).not.toHaveBeenCalled();
  });

  it('error path forwards errorCode from strategy submit', () => {
    const store = buildStore();
    const bankSync = buildBankSync();
    const strategy = monobankStrategy(() =>
      throwError(() => Object.assign(new Error('x'), {errorCode: 'MONOBANK_TOKEN_INVALID'}))
    );
    configure(bankSync);

    TestBed.runInInjectionContext(() => {
      connectEffects(store).connect({strategy, payload: undefined});
    });

    expect(store.setError).toHaveBeenCalledWith('MONOBANK_TOKEN_INVALID');
  });
});
