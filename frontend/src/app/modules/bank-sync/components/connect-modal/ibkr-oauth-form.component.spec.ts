import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {IbkrConnectStore} from '../../store/ibkr-connect/ibkr-connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {IbkrOauthFormComponent} from './ibkr-oauth-form.component';

const VALID_FORM = {
  consumerKey: 'finsentry',
  accessToken: '  token-abc  ',
  accessTokenSecret: 'secret-xyz',
  signatureKey: '-----BEGIN PRIVATE KEY-----sig-----END PRIVATE KEY-----',
  encryptionKey: '-----BEGIN PRIVATE KEY-----enc-----END PRIVATE KEY-----',
  dhParam: '-----BEGIN DH PARAMETERS-----dh-----END DH PARAMETERS-----',
};

function buildConnectStore(errorCode: Nullable<string> = null) {
  return {
    errorCode: signal<Nullable<string>>(errorCode),
    errorMessage: signal('IBKR account already connected'),
    isBusy: signal(false),
    connect: vi.fn(),
    resetError: vi.fn(),
  };
}

function buildIbkrStore(path: 'flex' | 'oauth' = 'oauth') {
  return {path: signal(path), setPath: vi.fn()};
}

function buildAccountsStore() {
  return {disconnectBinance: vi.fn(), disconnectIBKR: vi.fn()};
}

function buildStrategy(): ConnectStrategy {
  return {
    slug: 'ibkr',
    formComponent: class {} as unknown as ConnectStrategy['formComponent'],
    submit: vi.fn(),
  };
}

function configure(
  store: ReturnType<typeof buildConnectStore>,
  accounts: ReturnType<typeof buildAccountsStore>,
  strategy: ConnectStrategy,
  ibkr: ReturnType<typeof buildIbkrStore> = buildIbkrStore()
): void {
  TestBed.configureTestingModule({
    providers: [
      {provide: IbkrConnectStore, useValue: ibkr},
      {provide: ConnectStore, useValue: store},
      {provide: AccountsStore, useValue: accounts},
      {provide: CONNECT_STRATEGY, useValue: strategy},
    ],
  });
}

describe('IbkrOauthFormComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('submit() dispatches store.connect with trimmed, upper-cased OAuth artifacts', () => {
    const store = buildConnectStore();
    const strategy = buildStrategy();
    configure(store, buildAccountsStore(), strategy);

    const fixture = TestBed.createComponent(IbkrOauthFormComponent);
    const cmp = fixture.componentInstance;
    cmp.form.setValue(VALID_FORM);

    cmp.submit();

    expect(store.connect).toHaveBeenCalledWith({
      strategy,
      payload: {
        kind: 'oauth',
        payload: {
          consumerKey: 'FINSENTRY',
          accessToken: 'token-abc',
          accessTokenSecret: 'secret-xyz',
          signatureKey: VALID_FORM.signatureKey,
          encryptionKey: VALID_FORM.encryptionKey,
          dhParam: VALID_FORM.dhParam,
        },
      },
    });
  });

  it('submit() marks OAuth as the path of the attempt so its error shows here', () => {
    const ibkr = buildIbkrStore('flex');
    configure(buildConnectStore(), buildAccountsStore(), buildStrategy(), ibkr);

    const fixture = TestBed.createComponent(IbkrOauthFormComponent);
    fixture.componentInstance.form.setValue(VALID_FORM);
    fixture.componentInstance.submit();

    expect(ibkr.setPath).toHaveBeenCalledWith('oauth');
  });

  it('hides the shared connect error when the last attempt came from the Flex form', () => {
    const store = buildConnectStore('IBKR_DUPLICATE');
    configure(store, buildAccountsStore(), buildStrategy(), buildIbkrStore('flex'));

    const fixture = TestBed.createComponent(IbkrOauthFormComponent);

    expect(fixture.componentInstance.showsError()).toBe(false);
    expect(fixture.componentInstance.isDuplicateError()).toBe(false);
  });

  it('submit() blocks and marks touched when the form is invalid', () => {
    const store = buildConnectStore();
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(IbkrOauthFormComponent);
    fixture.componentInstance.submit();

    expect(store.connect).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.consumerKey.touched).toBe(true);
  });

  it('rejects a consumer key that is not exactly 9 characters', () => {
    const store = buildConnectStore();
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(IbkrOauthFormComponent);
    const cmp = fixture.componentInstance;
    cmp.form.setValue({...VALID_FORM, consumerKey: 'SHORT'});

    cmp.submit();

    expect(store.connect).not.toHaveBeenCalled();
    expect(cmp.form.controls.consumerKey.valid).toBe(false);
  });

  it('isDuplicateError flips when error code is IBKR_DUPLICATE', () => {
    const store = buildConnectStore('IBKR_DUPLICATE');
    configure(store, buildAccountsStore(), buildStrategy());

    const fixture = TestBed.createComponent(IbkrOauthFormComponent);
    expect(fixture.componentInstance.isDuplicateError()).toBe(true);
  });

  it('disconnectExisting calls accountsStore.disconnectIBKR, resets error, and clears the form', () => {
    const store = buildConnectStore('IBKR_DUPLICATE');
    const accounts = buildAccountsStore();
    configure(store, accounts, buildStrategy());

    const fixture = TestBed.createComponent(IbkrOauthFormComponent);
    const cmp = fixture.componentInstance;
    cmp.form.setValue(VALID_FORM);

    cmp.disconnectExisting();

    expect(accounts.disconnectIBKR).toHaveBeenCalledOnce();
    expect(store.resetError).toHaveBeenCalledOnce();
    expect(cmp.form.controls.consumerKey.value).toBe('');
    expect(cmp.form.controls.accessTokenSecret.value).toBe('');
  });
});
