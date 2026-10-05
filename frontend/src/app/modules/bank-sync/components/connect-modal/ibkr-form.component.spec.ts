import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {IbkrConnectStore} from '../../store/ibkr-connect/ibkr-connect.store';
import {type ConnectStrategy} from '../../strategies/connect-strategy';
import {CONNECT_STRATEGY} from '../../strategies/connect-strategy.token';
import {IbkrFormComponent} from './ibkr-form.component';

const PREVIEW = {
  accountId: 'U1234567',
  fromDate: '2025-01-01',
  toDate: '2025-12-31',
  generatedAtUtc: '2026-01-05T14:30:15Z',
  openPositionsCount: 3,
  cashCurrencies: ['USD'],
  tradesCount: 5,
  cashTransactionsCount: 2,
};

function buildIbkrStore(hasPreview = false) {
  return {
    path: signal<'flex' | 'oauth'>('flex'),
    hasPreview: signal(hasPreview),
    preview: signal(hasPreview ? PREVIEW : null),
    isValidating: signal(false),
    validationErrorMessage: signal(''),
    missingSections: signal<string[]>([]),
    setPath: vi.fn(),
    validate: vi.fn(),
    resetValidation: vi.fn(),
  };
}

function buildConnectStore() {
  return {
    errorCode: signal<Nullable<string>>(null),
    errorMessage: signal(''),
    isBusy: signal(false),
    connect: vi.fn(),
    setModalStep: vi.fn(),
    resetError: vi.fn(),
  };
}

function setup(ibkr = buildIbkrStore(), store = buildConnectStore()) {
  const strategy: ConnectStrategy = {
    slug: 'ibkr',
    formComponent: class {} as unknown as ConnectStrategy['formComponent'],
    submit: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      {provide: ConnectStore, useValue: store},
      {provide: AccountsStore, useValue: {disconnectIBKR: vi.fn()}},
      {provide: CONNECT_STRATEGY, useValue: strategy},
    ],
  });
  // The form provides its own page-scoped IbkrConnectStore; swap it for the fake.
  TestBed.overrideComponent(IbkrFormComponent, {
    set: {providers: [{provide: IbkrConnectStore, useValue: ibkr}]},
  });
  const fixture = TestBed.createComponent(IbkrFormComponent);
  return {fixture, cmp: fixture.componentInstance, ibkr, store, strategy};
}

describe('IbkrFormComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('asks for exactly two fields by default', () => {
    const {cmp} = setup();
    expect(Object.keys(cmp.form.controls)).toEqual(['token', 'queryId']);
  });

  it('check() validates the pair (spaces stripped from the token) before anything is saved', () => {
    const {cmp, ibkr, store} = setup();
    cmp.form.setValue({token: ' 123 456\n789 ', queryId: ' 987654 '});

    cmp.check();

    expect(ibkr.setPath).toHaveBeenCalledWith('flex');
    expect(ibkr.validate).toHaveBeenCalledWith({token: '123456789', queryId: '987654'});
    expect(store.connect).not.toHaveBeenCalled();
  });

  it('check() blocks and marks touched when the form is invalid', () => {
    const {cmp, ibkr} = setup();

    cmp.check();

    expect(ibkr.validate).not.toHaveBeenCalled();
    expect(cmp.form.controls.token.touched).toBe(true);
  });

  it('rejects a non-numeric query id', () => {
    const {cmp} = setup();
    cmp.form.setValue({token: 'tok', queryId: 'my-query'});
    expect(cmp.form.controls.queryId.valid).toBe(false);
  });

  it('confirm() saves through the connect store only after a successful preview', () => {
    const {cmp, store, strategy} = setup(buildIbkrStore(true));
    cmp.form.setValue({token: 'tok', queryId: '123456'});

    cmp.confirm();

    expect(store.connect).toHaveBeenCalledWith({
      strategy,
      payload: {kind: 'flex', payload: {token: 'tok', queryId: '123456'}},
    });
  });

  it('confirm() does nothing without a preview', () => {
    const {cmp, store} = setup(buildIbkrStore(false));
    cmp.form.setValue({token: 'tok', queryId: '123456'});

    cmp.confirm();

    expect(store.connect).not.toHaveBeenCalled();
  });

  it('edit() drops the preview and any connect error', () => {
    const {cmp, ibkr, store} = setup(buildIbkrStore(true));

    cmp.edit();

    expect(ibkr.resetValidation).toHaveBeenCalledOnce();
    expect(store.resetError).toHaveBeenCalledOnce();
  });

  it('back() returns to the type picker', () => {
    const {cmp, store} = setup();
    cmp.back();
    expect(store.setModalStep).toHaveBeenCalledWith('type-picker');
  });

  it('renders the account id and counts from the preview', () => {
    const {fixture} = setup(buildIbkrStore(true));
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('U1234567');
    expect(text).toContain('Open positions');
    expect(text).toContain('once a day');
  });

  it('collapses the OAuth path into an optional Advanced section', () => {
    const {fixture} = setup();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const summaries = Array.from(el.querySelectorAll('details > summary')).map(s => s.textContent);
    expect(summaries.some(t => t?.includes('Advanced: live prices (optional)'))).toBe(true);
    expect(el.querySelector('details[open]')).toBeNull();
  });
});
