import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {MonobankFormComponent} from './monobank-form.component';

function buildConnectStore(errorCode: Nullable<string> = null) {
  return {
    connectErrorCode: signal<Nullable<string>>(errorCode),
    errorMessage: signal('Token already in use'),
    isConnectLoading: signal(false),
    connect: vi.fn(),
    setModalStep: vi.fn(),
    resetError: vi.fn(),
  };
}

function buildAccountsStore() {
  return {disconnectMonobank: vi.fn()};
}

function buildStrategy(): ConnectStrategy {
  return {
    slug: 'monobank',
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

describe('MonobankFormComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('rejects an invalid token format without dispatching', () => {
    const store = buildConnectStore();
    const strategy = buildStrategy();
    configure(store, buildAccountsStore(), strategy);

    const fixture = TestBed.createComponent(MonobankFormComponent);
    const cmp = fixture.componentInstance;
    cmp.tokenControl.setValue('not-a-monobank-token');
    cmp.submit();

    expect(store.connect).not.toHaveBeenCalled();
    expect(cmp.tokenControl.touched).toBe(true);
  });

  it('trims pasted whitespace and marks the control touched', () => {
    const store = buildConnectStore();
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(MonobankFormComponent);
    const cmp = fixture.componentInstance;
    const event = new ClipboardEvent('paste', {clipboardData: new DataTransfer()});
    event.clipboardData?.setData('text', '  uABCDEFGHIJKLMNOPQRSTUVWXYZ012  ');

    cmp.onPaste(event);

    expect(cmp.tokenControl.value).toBe('uABCDEFGHIJKLMNOPQRSTUVWXYZ012');
    expect(cmp.tokenControl.touched).toBe(true);
  });

  it('dispatches connect with the trimmed token on valid submit', () => {
    const store = buildConnectStore();
    const strategy = buildStrategy();
    configure(store, buildAccountsStore(), strategy);

    const fixture = TestBed.createComponent(MonobankFormComponent);
    const cmp = fixture.componentInstance;
    cmp.tokenControl.setValue('uABCDEFGHIJKLMNOPQRSTUVWXYZ012');
    cmp.submit();

    expect(store.connect).toHaveBeenCalledWith({
      strategy,
      payload: {token: 'uABCDEFGHIJKLMNOPQRSTUVWXYZ012'},
    });
  });

  it('isDuplicateError flips when error code is MONOBANK_TOKEN_DUPLICATE', () => {
    const store = buildConnectStore('MONOBANK_TOKEN_DUPLICATE');
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(MonobankFormComponent);
    expect(fixture.componentInstance.isDuplicateError()).toBe(true);
  });

  it('disconnectExisting calls accountsStore.disconnectMonobank and resets error', () => {
    const store = buildConnectStore('MONOBANK_TOKEN_DUPLICATE');
    const accounts = buildAccountsStore();
    configure(store, accounts, buildStrategy());

    const fixture = TestBed.createComponent(MonobankFormComponent);
    fixture.componentInstance.tokenControl.setValue('uABCDEFGHIJKLMNOPQRSTUVWXYZ012');
    fixture.componentInstance.disconnectExisting();

    expect(accounts.disconnectMonobank).toHaveBeenCalledOnce();
    expect(store.resetError).toHaveBeenCalledOnce();
    expect(fixture.componentInstance.tokenControl.value).toBe('');
  });
});
