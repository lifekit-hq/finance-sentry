import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {RevolutXFormComponent} from './revolut-x-form.component';

const PEM = '-----BEGIN PRIVATE KEY-----MC4CAQAw-----END PRIVATE KEY-----';

function buildConnectStore(errorCode: Nullable<string> = null) {
  return {
    errorCode: signal<Nullable<string>>(errorCode),
    errorMessage: signal('Revolut X account already connected'),
    isBusy: signal(false),
    connect: vi.fn(),
    setModalStep: vi.fn(),
    resetError: vi.fn(),
  };
}

function buildAccountsStore() {
  return {disconnectRevolutX: vi.fn(), disconnectBinance: vi.fn()};
}

function buildStrategy(): ConnectStrategy {
  return {
    slug: 'revolut_x',
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

describe('RevolutXFormComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not dispatch without the private key', () => {
    const store = buildConnectStore();
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(RevolutXFormComponent);
    fixture.componentInstance.form.controls.apiKey.setValue('keyOnly');
    fixture.componentInstance.submit();

    expect(store.connect).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.privateKey.touched).toBe(true);
  });

  it('dispatches connect with the trimmed API key and private key', () => {
    const store = buildConnectStore();
    const strategy = buildStrategy();
    configure(store, buildAccountsStore(), strategy);

    const fixture = TestBed.createComponent(RevolutXFormComponent);
    fixture.componentInstance.form.setValue({apiKey: '  abc123  ', privateKey: `  ${PEM}  `});
    fixture.componentInstance.submit();

    expect(store.connect).toHaveBeenCalledWith({
      strategy,
      payload: {apiKey: 'abc123', privateKey: PEM},
    });
  });

  it('isDuplicateError flips when the backend emits ALREADY_CONNECTED', () => {
    configure(buildConnectStore('ALREADY_CONNECTED'), buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(RevolutXFormComponent);
    expect(fixture.componentInstance.isDuplicateError()).toBe(true);
  });

  it('isDuplicateError stays off for a rejected key', () => {
    configure(buildConnectStore('INVALID_CREDENTIALS'), buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(RevolutXFormComponent);
    expect(fixture.componentInstance.isDuplicateError()).toBe(false);
  });

  it('back() returns to the provider picker', () => {
    const store = buildConnectStore();
    configure(store, buildAccountsStore(), buildStrategy());

    TestBed.createComponent(RevolutXFormComponent).componentInstance.back();

    expect(store.setModalStep).toHaveBeenCalledWith('provider-picker');
  });

  it('disconnectExisting disconnects Revolut X only and resets error/form', () => {
    const store = buildConnectStore('ALREADY_CONNECTED');
    const accounts = buildAccountsStore();
    configure(store, accounts, buildStrategy());

    const fixture = TestBed.createComponent(RevolutXFormComponent);
    fixture.componentInstance.form.setValue({apiKey: 'k', privateKey: PEM});
    fixture.componentInstance.disconnectExisting();

    expect(accounts.disconnectRevolutX).toHaveBeenCalledOnce();
    expect(accounts.disconnectBinance).not.toHaveBeenCalled();
    expect(store.resetError).toHaveBeenCalledOnce();
    expect(fixture.componentInstance.form.getRawValue()).toEqual({apiKey: '', privateKey: ''});
  });
});
