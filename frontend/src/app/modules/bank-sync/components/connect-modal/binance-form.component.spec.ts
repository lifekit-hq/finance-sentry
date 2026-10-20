import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {BinanceFormComponent} from './binance-form.component';

function buildConnectStore(errorCode: Nullable<string> = null) {
  return {
    connectErrorCode: signal<Nullable<string>>(errorCode),
    errorMessage: signal('Account already connected'),
    isConnectLoading: signal(false),
    connect: vi.fn(),
    setModalStep: vi.fn(),
    resetError: vi.fn(),
  };
}

function buildAccountsStore() {
  return {disconnectBinance: vi.fn(), disconnectIBKR: vi.fn()};
}

function buildStrategy(): ConnectStrategy {
  return {
    slug: 'binance',
    formComponent: class {} as unknown as ConnectStrategy['formComponent'],
    submit: vi.fn(),
  };
}

function configure(
  store: ReturnType<typeof buildConnectStore>,
  accounts: ReturnType<typeof buildAccountsStore>,
  strategy: ConnectStrategy
): void {
  TestBed.configureTestingModule({
    providers: [
      {provide: ConnectStore, useValue: store},
      {provide: AccountsStore, useValue: accounts},
      {provide: CONNECT_STRATEGY, useValue: strategy},
    ],
  });
}

describe('BinanceFormComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not dispatch when either field is empty', () => {
    const store = buildConnectStore();
    const strategy = buildStrategy();
    configure(store, buildAccountsStore(), strategy);

    const fixture = TestBed.createComponent(BinanceFormComponent);
    fixture.componentInstance.form.controls.apiKey.setValue('keyOnly');
    fixture.componentInstance.submit();

    expect(store.connect).not.toHaveBeenCalled();
  });

  it('dispatches connect with trimmed key + secret when both are filled', () => {
    const store = buildConnectStore();
    const strategy = buildStrategy();
    configure(store, buildAccountsStore(), strategy);

    const fixture = TestBed.createComponent(BinanceFormComponent);
    fixture.componentInstance.form.setValue({
      apiKey: '  abc123  ',
      apiSecret: '  s3cret  ',
    });
    fixture.componentInstance.submit();

    expect(store.connect).toHaveBeenCalledWith({
      strategy,
      payload: {apiKey: 'abc123', apiSecret: 's3cret'},
    });
  });

  it('isDuplicateError flips when error code is BINANCE_DUPLICATE', () => {
    const store = buildConnectStore('BINANCE_DUPLICATE');
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(BinanceFormComponent);
    expect(fixture.componentInstance.isDuplicateError()).toBe(true);
  });

  it('isDuplicateError flips when the backend emits ALREADY_CONNECTED', () => {
    const store = buildConnectStore('ALREADY_CONNECTED');
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(BinanceFormComponent);
    expect(fixture.componentInstance.isDuplicateError()).toBe(true);
  });

  it('disconnectExisting calls accountsStore.disconnectBinance and resets error/form', () => {
    const store = buildConnectStore('BINANCE_DUPLICATE');
    const accounts = buildAccountsStore();
    configure(store, accounts, buildStrategy());

    const fixture = TestBed.createComponent(BinanceFormComponent);
    fixture.componentInstance.form.setValue({apiKey: 'k', apiSecret: 's'});
    fixture.componentInstance.disconnectExisting();

    expect(accounts.disconnectBinance).toHaveBeenCalledOnce();
    expect(store.resetError).toHaveBeenCalledOnce();
    expect(fixture.componentInstance.form.getRawValue()).toEqual({apiKey: '', apiSecret: ''});
  });
});
