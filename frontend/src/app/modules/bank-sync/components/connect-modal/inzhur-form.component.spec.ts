import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {INZHUR_SESSION_MAX_LENGTH} from '../../constants/inzhur/inzhur.constants';
import {ConnectStore} from '../../store/connect/connect.store';
import {InzhurConnectStore} from '../../store/inzhur-connect/inzhur-connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {InzhurFormComponent} from './inzhur-form.component';

// Placeholder value only; no real cookie belongs in a test.
const PASTED = 'fake-refresh';

function buildInzhurStore(opts: {isReconnect?: boolean} = {}) {
  return {
    asyncStatus: signal('success'),
    loadErrorMessage: signal(''),
    isReconnect: signal(opts.isReconnect ?? false),
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

  it('asks for the session in a masked field that is never autofilled', () => {
    const {fixture} = setup();
    const host = (fixture.nativeElement as HTMLElement).querySelector('cmn-input');

    expect(host?.getAttribute('type')).toBe('password');
    expect(host?.getAttribute('autocomplete')).toBe('off');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Paste Inzhur session');
  });

  it('does not connect while the field is empty or blank', () => {
    const {cmp, store} = setup();

    cmp.connect();
    cmp.sessionForm.setValue({refreshToken: '   '});
    cmp.connect();

    expect(store.connect).not.toHaveBeenCalled();
    expect(cmp.sessionForm.touched).toBe(true);
  });

  it('does not connect a paste longer than any cookie', () => {
    const {cmp, store} = setup();
    cmp.sessionForm.setValue({refreshToken: 'x'.repeat(INZHUR_SESSION_MAX_LENGTH + 1)});

    cmp.connect();

    expect(store.connect).not.toHaveBeenCalled();
  });

  it('connect() hands the trimmed paste to the connect store', () => {
    const {cmp, store, strategy} = setup();
    cmp.sessionForm.setValue({refreshToken: ` ${PASTED} `});

    cmp.connect();

    expect(store.connect).toHaveBeenCalledWith({strategy, payload: {refreshToken: PASTED}});
  });

  it('says why when the saved session ended', () => {
    const {fixture} = setup(buildInzhurStore({isReconnect: true}));

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Inzhur needs a new session'
    );
  });

  it('back() returns to the broker picker', () => {
    const {cmp, store} = setup();

    cmp.back();

    expect(store.setModalStep).toHaveBeenCalledWith('provider-picker');
  });
});
