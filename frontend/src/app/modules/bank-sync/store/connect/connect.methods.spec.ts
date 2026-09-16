import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {connectMethods} from './connect.methods';
import {initialConnectState} from './connect.state';

describe('connectMethods', () => {
  it('selectProvider updates provider and clears error/status', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.setError('X');

    methods.selectProvider('monobank');

    expect(state.selectedProvider()).toBe('monobank');
    expect(state.status()).toBe('idle');
    expect(state.errorCode()).toBeNull();
    expect(state.statusMessage()).toBeNull();
  });

  it('selectInstitutionType(crypto) resets error status and opens the provider picker', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.selectProvider('monobank');
    methods.setError('X');

    methods.selectInstitutionType('crypto');

    expect(state.modalStep()).toBe('provider-picker');
    expect(state.institutionType()).toBe('crypto');
    expect(state.selectedProvider()).toBe('monobank');
    expect(state.status()).toBe('idle');
    expect(state.errorCode()).toBeNull();
    expect(state.statusMessage()).toBeNull();
  });

  it('selectInstitutionType(broker) resets error status and selects ibkr', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.setError('X');

    methods.selectInstitutionType('broker');

    expect(state.modalStep()).toBe('ibkr-form');
    expect(state.selectedProvider()).toBe('ibkr');
    expect(state.status()).toBe('idle');
    expect(state.errorCode()).toBeNull();
  });

  it('selectInstitutionType(bank) keeps the current provider until a bank is picked', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.selectProvider('monobank');

    methods.selectInstitutionType('bank');

    expect(state.modalStep()).toBe('provider-picker');
    expect(state.selectedProvider()).toBe('monobank');
    expect(state.status()).toBe('idle');
  });

  it('setModalStep resets error status on navigation between steps', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.setError('MONOBANK_TOKEN_INVALID');

    methods.setModalStep('provider-picker');

    expect(state.modalStep()).toBe('provider-picker');
    expect(state.status()).toBe('idle');
    expect(state.errorCode()).toBeNull();
    expect(state.statusMessage()).toBeNull();
  });

  it('selectPickedProvider resets error status and sets the provider', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.setError('X');

    methods.selectPickedProvider('truelayer');

    expect(state.selectedProvider()).toBe('truelayer');
    expect(state.institutionType()).toBe('bank');
    expect(state.modalStep()).toBe('truelayer-picker');
    expect(state.status()).toBe('idle');
    expect(state.errorCode()).toBeNull();
  });

  it.each([
    ['monobank', 'monobank-form', 'bank'],
    ['binance', 'binance-form', 'crypto'],
    ['revolut_x', 'revolut-x-form', 'crypto'],
  ] as const)('selectPickedProvider(%s) opens %s under the %s type', (slug, step, type) => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);

    methods.selectPickedProvider(slug);

    expect(state.selectedProvider()).toBe(slug);
    expect(state.modalStep()).toBe(step);
    expect(state.institutionType()).toBe(type);
  });

  it('setInitializing clears error and statusMessage', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.setError('X');
    methods.setSyncing('old');

    methods.setInitializing();

    expect(state.status()).toBe('initializing');
    expect(state.errorCode()).toBeNull();
    expect(state.statusMessage()).toBeNull();
  });

  it('setReady transitions status and clears error', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.setError('X');

    methods.setReady();

    expect(state.status()).toBe('ready');
    expect(state.errorCode()).toBeNull();
  });

  it('setSyncing carries the message and clears errors', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);

    methods.setSyncing('wait');

    expect(state.status()).toBe('syncing');
    expect(state.statusMessage()).toBe('wait');
    expect(state.errorCode()).toBeNull();
  });

  it('setPolling updates status and message', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);

    methods.setPolling('polling');

    expect(state.status()).toBe('polling');
    expect(state.statusMessage()).toBe('polling');
  });

  it('setSuccess resets transient fields', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);
    methods.setSyncing('wait');

    methods.setSuccess();

    expect(state.status()).toBe('success');
    expect(state.statusMessage()).toBeNull();
    expect(state.errorCode()).toBeNull();
  });

  it('setError stores the code', () => {
    const state = signalState(initialConnectState);
    const methods = connectMethods(state);

    methods.setError('BOOM');

    expect(state.status()).toBe('error');
    expect(state.errorCode()).toBe('BOOM');
  });
});
