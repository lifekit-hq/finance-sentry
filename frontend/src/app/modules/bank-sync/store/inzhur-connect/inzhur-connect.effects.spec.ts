import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ErrorMessageService} from '@lifekit-hq/core';
import {of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type InzhurConnectionStatus,
  type InzhurConnectResult,
} from '../../models/inzhur/inzhur.model';
import {InzhurService} from '../../services/inzhur.service';
import {AccountsStore} from '../accounts/accounts.store';
import {ConnectStore} from '../connect/connect.store';
import {InzhurConnectStore} from './inzhur-connect.store';

const SAVED: InzhurConnectionStatus = {
  status: 'reauth_required',
  hasSavedCredentials: true,
  lastSyncAt: '2026-10-06T07:00:00Z',
  sessionStartedAt: '2026-10-01T07:00:00Z',
  loginAvailable: true,
};
const NEW: InzhurConnectionStatus = {
  status: 'not_connected',
  hasSavedCredentials: false,
  lastSyncAt: null,
  sessionStartedAt: null,
  loginAvailable: true,
};
const CODE_REQUIRED: InzhurConnectResult = {
  status: 'code_required',
  codeExpiresAt: '2026-10-07T10:04:00Z',
  attemptsLeft: null,
};
// Placeholder values only; no real phone or password belongs in a test.
const TYPED = {phone: '+000 000 0000', password: 'placeholder'};

describe('InzhurConnectStore', () => {
  let inzhur: {getStatus: ReturnType<typeof vi.fn>; startLogin: ReturnType<typeof vi.fn>};
  let connect: {
    selectProvider: ReturnType<typeof vi.fn>;
    setInstitutionType: ReturnType<typeof vi.fn>;
    setSuccess: ReturnType<typeof vi.fn>;
  };
  let accounts: {load: ReturnType<typeof vi.fn>};
  let resolve: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    inzhur = {getStatus: vi.fn().mockReturnValue(of(NEW)), startLogin: vi.fn()};
    connect = {selectProvider: vi.fn(), setInstitutionType: vi.fn(), setSuccess: vi.fn()};
    accounts = {load: vi.fn()};
    resolve = vi.fn((code: string) => (code === 'INZHUR_LOGIN_LIMIT' ? 'Limit reached.' : null));
    TestBed.configureTestingModule({
      providers: [
        InzhurConnectStore,
        {provide: InzhurService, useValue: inzhur},
        {provide: ConnectStore, useValue: connect},
        {provide: AccountsStore, useValue: accounts},
        {provide: ErrorMessageService, useValue: {resolve}},
      ],
    });
  });

  it('loads the connection on init and asks for a phone and password when none are saved', () => {
    const store = TestBed.inject(InzhurConnectStore);

    expect(inzhur.getStatus).toHaveBeenCalledTimes(1);
    expect(store.asyncStatus()).toBe('success');
    expect(store.asksForCredentials()).toBe(true);
    expect(store.isReconnect()).toBe(false);
    expect(store.loginAvailable()).toBe(true);
  });

  it('skips the phone and password on a reconnect with saved ones, until the owner edits them', () => {
    inzhur.getStatus.mockReturnValue(of(SAVED));
    const store = TestBed.inject(InzhurConnectStore);

    expect(store.isReconnect()).toBe(true);
    expect(store.asksForCredentials()).toBe(false);

    store.editCredentials();

    expect(store.asksForCredentials()).toBe(true);
  });

  it('reports a failed status load as an error state', () => {
    inzhur.getStatus.mockReturnValue(throwError(() => new Error('down')));
    const store = TestBed.inject(InzhurConnectStore);

    expect(store.asyncStatus()).toBe('error');
    expect(store.loadErrorMessage()).toContain("Couldn't load your Inzhur connection");
  });

  it('start() moves to the code step with the code expiry when Inzhur sends an SMS', () => {
    inzhur.startLogin.mockReturnValue(of(CODE_REQUIRED));
    const store = TestBed.inject(InzhurConnectStore);

    store.start(TYPED);

    expect(inzhur.startLogin).toHaveBeenCalledWith(TYPED);
    expect(store.step()).toBe('code');
    expect(store.codeExpiresAt()).toBe(CODE_REQUIRED.codeExpiresAt);
    expect(store.isStarting()).toBe(false);
    expect(connect.setSuccess).not.toHaveBeenCalled();
  });

  it('start() finishes the connect flow when Inzhur signs in without a code', () => {
    inzhur.startLogin.mockReturnValue(
      of({status: 'connected', codeExpiresAt: null, attemptsLeft: null})
    );
    const store = TestBed.inject(InzhurConnectStore);

    store.start({phone: null, password: null});

    expect(connect.selectProvider).toHaveBeenCalledWith('inzhur');
    expect(connect.setInstitutionType).toHaveBeenCalledWith('broker');
    expect(connect.setSuccess).toHaveBeenCalled();
    expect(accounts.load).toHaveBeenCalled();
    expect(store.step()).toBe('credentials');
  });

  it('start() ignores a second tap while the first sign-in is in flight', () => {
    const pending = new Subject<InzhurConnectResult>();
    inzhur.startLogin.mockReturnValue(pending);
    const store = TestBed.inject(InzhurConnectStore);

    store.start(TYPED);
    store.start(TYPED);

    expect(inzhur.startLogin).toHaveBeenCalledTimes(1);
    expect(store.isStarting()).toBe(true);
  });

  it('start() maps a registered error code to its message', () => {
    inzhur.startLogin.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 400, error: {errorCode: 'INZHUR_LOGIN_LIMIT'}})
      )
    );
    const store = TestBed.inject(InzhurConnectStore);

    store.start(TYPED);

    expect(store.startErrorMessage()).toBe('Limit reached.');
    expect(store.step()).toBe('credentials');
  });

  it('start() falls back to a generic message for an unregistered failure', () => {
    inzhur.startLogin.mockReturnValue(throwError(() => new Error('boom')));
    const store = TestBed.inject(InzhurConnectStore);

    store.start(TYPED);

    expect(store.startErrorMessage()).toContain("Couldn't sign in to Inzhur");
  });

  it('backToCredentials() leaves the code step without starting a new sign-in', () => {
    inzhur.startLogin.mockReturnValue(of(CODE_REQUIRED));
    const store = TestBed.inject(InzhurConnectStore);
    store.start(TYPED);

    store.backToCredentials();

    expect(store.step()).toBe('credentials');
    expect(store.codeExpiresAt()).toBeNull();
    expect(inzhur.startLogin).toHaveBeenCalledTimes(1);
  });
});
