import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {BankSyncService} from '../../services/bank-sync.service';
import {ConnectStore} from './connect.store';

function createStore() {
  TestBed.configureTestingModule({
    providers: [
      ConnectStore,
      {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      {provide: BankSyncService, useValue: {}},
    ],
  });
  return TestBed.inject(ConnectStore);
}

describe('connectMethods', () => {
  it('selectProvider updates provider and clears error/status', () => {
    const store = createStore();
    store.setError('X');

    store.selectProvider('monobank');

    expect(store.selectedProvider()).toBe('monobank');
    expect(store.connectStatus()).toBe('idle');
    expect(store.connectErrorCode()).toBeNull();
    expect(store.statusMessage()).toBeNull();
  });

  it('selectInstitutionType(crypto) resets error status and opens the provider picker', () => {
    const store = createStore();
    store.selectProvider('monobank');
    store.setError('X');

    store.selectInstitutionType('crypto');

    expect(store.modalStep()).toBe('provider-picker');
    expect(store.institutionType()).toBe('crypto');
    expect(store.selectedProvider()).toBe('monobank');
    expect(store.connectStatus()).toBe('idle');
    expect(store.connectErrorCode()).toBeNull();
    expect(store.statusMessage()).toBeNull();
  });

  it('selectInstitutionType(broker) resets error status and offers the broker picker', () => {
    const store = createStore();
    store.setError('X');

    store.selectInstitutionType('broker');

    expect(store.modalStep()).toBe('provider-picker');
    expect(store.institutionType()).toBe('broker');
    expect(store.connectStatus()).toBe('idle');
    expect(store.connectErrorCode()).toBeNull();
  });

  it.each([
    ['ibkr', 'ibkr-form'],
    ['inzhur', 'inzhur-form'],
  ] as const)('selectPickedProvider(%s) opens %s as a broker', (slug, step) => {
    const store = createStore();

    store.selectPickedProvider(slug);

    expect(store.modalStep()).toBe(step);
    expect(store.selectedProvider()).toBe(slug);
    expect(store.institutionType()).toBe('broker');
  });

  it('selectInstitutionType(bank) keeps the current provider until a bank is picked', () => {
    const store = createStore();
    store.selectProvider('monobank');

    store.selectInstitutionType('bank');

    expect(store.modalStep()).toBe('provider-picker');
    expect(store.selectedProvider()).toBe('monobank');
    expect(store.connectStatus()).toBe('idle');
  });

  it('setModalStep resets error status on navigation between steps', () => {
    const store = createStore();
    store.setError('MONOBANK_TOKEN_INVALID');

    store.setModalStep('provider-picker');

    expect(store.modalStep()).toBe('provider-picker');
    expect(store.connectStatus()).toBe('idle');
    expect(store.connectErrorCode()).toBeNull();
    expect(store.statusMessage()).toBeNull();
  });

  it('selectPickedProvider resets error status and sets the provider', () => {
    const store = createStore();
    store.setError('X');

    store.selectPickedProvider('truelayer');

    expect(store.selectedProvider()).toBe('truelayer');
    expect(store.institutionType()).toBe('bank');
    expect(store.modalStep()).toBe('truelayer-picker');
    expect(store.connectStatus()).toBe('idle');
    expect(store.connectErrorCode()).toBeNull();
  });

  it.each([
    ['monobank', 'monobank-form', 'bank'],
    ['binance', 'binance-form', 'crypto'],
    ['revolut_x', 'revolut-x-form', 'crypto'],
  ] as const)('selectPickedProvider(%s) opens %s under the %s type', (slug, step, type) => {
    const store = createStore();

    store.selectPickedProvider(slug);

    expect(store.selectedProvider()).toBe(slug);
    expect(store.modalStep()).toBe(step);
    expect(store.institutionType()).toBe(type);
  });

  it('setSyncing carries the message and clears errors', () => {
    const store = createStore();

    store.setSyncing('wait');

    expect(store.connectStatus()).toBe('loading');
    expect(store.statusMessage()).toBe('wait');
    expect(store.connectErrorCode()).toBeNull();
  });

  it('setPolling updates status and message', () => {
    const store = createStore();

    store.setPolling('polling');

    expect(store.connectStatus()).toBe('loading');
    expect(store.statusMessage()).toBe('polling');
  });

  it('setSuccess resets transient fields', () => {
    const store = createStore();
    store.setSyncing('wait');

    store.setSuccess();

    expect(store.connectStatus()).toBe('success');
    expect(store.statusMessage()).toBeNull();
    expect(store.connectErrorCode()).toBeNull();
  });

  it('setError stores the code', () => {
    const store = createStore();

    store.setError('BOOM');

    expect(store.connectStatus()).toBe('error');
    expect(store.connectErrorCode()).toBe('BOOM');
  });
});
