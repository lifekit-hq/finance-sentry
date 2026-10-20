import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type Provider} from '../../../../shared/models/provider/provider.model';
import {type ModalStep} from '../../models/connect/connect.model';
import {connectComputed} from './connect.computed';

function build(
  overrides: Partial<{
    selectedProvider: Provider;
    hasError: boolean;
    errorCode: Nullable<string>;
    modalStep: ModalStep;
  }> = {}
) {
  return {
    selectedProvider: signal<Provider>(overrides.selectedProvider ?? 'monobank'),
    hasConnectError: signal(overrides.hasError ?? false),
    connectErrorCode: signal<Nullable<string>>(overrides.errorCode ?? null),
    modalStep: signal<ModalStep>(overrides.modalStep ?? 'closed'),
  };
}

describe('connectComputed', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  it('errorMessage is empty when status is not error', () => {
    const store = build({errorCode: 'ANY'});
    TestBed.runInInjectionContext(() => {
      expect(connectComputed(store).errorMessage()).toBe('');
    });
  });

  it('errorMessage resolves known monobank code via registry', () => {
    const store = build({
      hasError: true,
      errorCode: 'MONOBANK_TOKEN_DUPLICATE',
      selectedProvider: 'monobank',
    });
    TestBed.runInInjectionContext(() => {
      expect(connectComputed(store).errorMessage()).toContain('already connected');
    });
  });

  it('errorMessage falls back to monobank default when code is unknown', () => {
    const store = build({hasError: true, errorCode: 'UNKNOWN', selectedProvider: 'monobank'});
    TestBed.runInInjectionContext(() => {
      expect(connectComputed(store).errorMessage()).toContain('Failed to connect Monobank');
    });
  });

  it('errorMessage resolves the real backend INVALID_CREDENTIALS code for binance', () => {
    const store = build({
      hasError: true,
      errorCode: 'INVALID_CREDENTIALS',
      selectedProvider: 'binance',
    });
    TestBed.runInInjectionContext(() => {
      expect(connectComputed(store).errorMessage()).toContain('read-only API key');
    });
  });

  it('errorMessage resolves the real backend INVALID_CREDENTIALS code for ibkr', () => {
    const store = build({
      hasError: true,
      errorCode: 'INVALID_CREDENTIALS',
      selectedProvider: 'ibkr',
    });
    TestBed.runInInjectionContext(() => {
      expect(connectComputed(store).errorMessage()).toContain('IB Gateway');
    });
  });

  it('errorMessage resolves the real backend ALREADY_CONNECTED code for binance', () => {
    const store = build({
      hasError: true,
      errorCode: 'ALREADY_CONNECTED',
      selectedProvider: 'binance',
    });
    TestBed.runInInjectionContext(() => {
      expect(connectComputed(store).errorMessage()).toContain('Binance account already connected');
    });
  });
});
