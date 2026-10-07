import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ConnectStore} from '../../store/connect/connect.store';
import {type InzhurFormStep} from '../../store/inzhur-connect/inzhur-connect.state';
import {InzhurConnectStore} from '../../store/inzhur-connect/inzhur-connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {InzhurFormComponent} from './inzhur-form.component';

// Placeholder values only; no real phone or password belongs in a test.
const PHONE = '+000 000 0000';
const PASSWORD = 'placeholder';

function buildInzhurStore(opts: {asksForCredentials?: boolean; step?: InzhurFormStep} = {}) {
  return {
    asyncStatus: signal('success'),
    loadErrorMessage: signal(''),
    loginAvailable: signal(true),
    isReconnect: signal(false),
    asksForCredentials: signal(opts.asksForCredentials ?? true),
    hasSavedCredentials: signal(!(opts.asksForCredentials ?? true)),
    isStarting: signal(false),
    startErrorMessage: signal(''),
    step: signal<InzhurFormStep>(opts.step ?? 'credentials'),
    codeExpiresAt: signal<Nullable<string>>(null),
    start: vi.fn(),
    editCredentials: vi.fn(),
    backToCredentials: vi.fn(),
  };
}

function buildConnectStore() {
  return {
    errorMessage: signal(''),
    isBusy: signal(false),
    connect: vi.fn(),
    setModalStep: vi.fn(),
    resetError: vi.fn(),
  };
}

function setup(inzhur = buildInzhurStore(), store = buildConnectStore()) {
  const strategy: ConnectStrategy = {
    slug: 'inzhur',
    formComponent: class {} as unknown as ConnectStrategy['formComponent'],
    submit: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      {provide: ConnectStore, useValue: store},
      {provide: CONNECT_STRATEGY, useValue: strategy},
    ],
  });
  // The form provides its own page-scoped InzhurConnectStore; swap it for the fake.
  TestBed.overrideComponent(InzhurFormComponent, {
    set: {providers: [{provide: InzhurConnectStore, useValue: inzhur}]},
  });
  const fixture = TestBed.createComponent(InzhurFormComponent);
  fixture.detectChanges();
  return {fixture, cmp: fixture.componentInstance, inzhur, store, strategy};
}

describe('InzhurFormComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not start a sign-in while the phone or password is missing', () => {
    const {cmp, inzhur} = setup();

    cmp.sendCode();

    expect(inzhur.start).not.toHaveBeenCalled();
    expect(cmp.credentialsForm.touched).toBe(true);
  });

  it('rejects a phone that is not a phone number', () => {
    const {cmp, inzhur} = setup();
    cmp.credentialsForm.setValue({phone: 'not a phone', password: PASSWORD});

    cmp.sendCode();

    expect(inzhur.start).not.toHaveBeenCalled();
  });

  it('sendCode() starts the sign-in with the typed phone and password', () => {
    const {cmp, inzhur, store} = setup();
    cmp.credentialsForm.setValue({phone: ` ${PHONE} `, password: PASSWORD});

    cmp.sendCode();

    expect(store.resetError).toHaveBeenCalled();
    expect(inzhur.start).toHaveBeenCalledWith({phone: PHONE, password: PASSWORD});
  });

  it('sendCode() reuses the saved phone and password without asking for them', () => {
    const {cmp, inzhur, fixture} = setup(buildInzhurStore({asksForCredentials: false}));

    cmp.sendCode();

    expect(inzhur.start).toHaveBeenCalledWith({phone: null, password: null});
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('input[type="password"]')
    ).toBeNull();
  });

  it('verify() submits the SMS code (spaces dropped) through the connect store', () => {
    const {cmp, store, strategy} = setup(buildInzhurStore({step: 'code'}));
    cmp.codeForm.setValue({code: '000 000'});

    cmp.verify();

    expect(store.connect).toHaveBeenCalledWith({strategy, payload: {code: '000000'}});
  });

  it('verify() does nothing without a well-formed code', () => {
    const {cmp, store} = setup(buildInzhurStore({step: 'code'}));
    cmp.codeForm.setValue({code: 'abc'});

    cmp.verify();

    expect(store.connect).not.toHaveBeenCalled();
  });

  it('asks for the code with a one-time-code numeric field', () => {
    const {fixture} = setup(buildInzhurStore({step: 'code'}));
    const host = (fixture.nativeElement as HTMLElement).querySelector('cmn-input');

    expect(host?.getAttribute('inputmode')).toBe('numeric');
    expect(host?.getAttribute('autocomplete')).toBe('one-time-code');
  });

  it('startOver() returns to the first step and clears the code', () => {
    const {cmp, inzhur, store} = setup(buildInzhurStore({step: 'code'}));
    cmp.codeForm.setValue({code: '000000'});

    cmp.startOver();

    expect(cmp.codeForm.getRawValue().code).toBe('');
    expect(store.resetError).toHaveBeenCalled();
    expect(inzhur.backToCredentials).toHaveBeenCalled();
    expect(inzhur.start).not.toHaveBeenCalled();
  });

  it('back() returns to the broker picker', () => {
    const {cmp, store} = setup();

    cmp.back();

    expect(store.setModalStep).toHaveBeenCalledWith('provider-picker');
  });
});
