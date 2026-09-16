import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {type Provider} from '../../../../shared/models/provider/provider.model';
import {type ModalStep} from '../../models/connect/connect.model';
import {AccountsStore} from '../accounts/accounts.store';
import {type ConnectStatus} from './connect.state';

interface StateSignals {
  selectedProvider: Signal<Provider>;
  status: Signal<ConnectStatus>;
  errorCode: Signal<Nullable<string>>;
  statusMessage: Signal<Nullable<string>>;
  modalStep: Signal<ModalStep>;
}

const DEFAULT_BINANCE_ERROR = 'Failed to connect Binance account. Please check your API keys.';
const DEFAULT_REVOLUT_X_ERROR =
  'Failed to connect Revolut X. Please check your API key and Ed25519 private key.';
const DEFAULT_IBKR_ERROR = 'Failed to connect IBKR account. Please check your credentials.';
const DEFAULT_MONOBANK_ERROR = 'Failed to connect Monobank account. Please try again.';

function mapErrorByProvider(
  code: Nullable<string>,
  provider: Provider,
  resolved: Nullable<string>
): string {
  if (resolved) {
    return resolved;
  }
  switch (provider) {
    case 'binance':
      return DEFAULT_BINANCE_ERROR;
    case 'revolut_x':
      return DEFAULT_REVOLUT_X_ERROR;
    case 'ibkr':
      return DEFAULT_IBKR_ERROR;
    default:
      return DEFAULT_MONOBANK_ERROR;
  }
}

const PROVIDER_SLUGS: readonly Provider[] = [
  'monobank',
  'truelayer',
  'binance',
  'revolut_x',
  'ibkr',
];

function resolveForProvider(
  errorMessages: ErrorMessageService,
  code: Nullable<string>,
  provider: Provider
): Nullable<string> {
  if (!code) {
    return null;
  }
  // Backend emits generic codes (e.g. INVALID_CREDENTIALS from both Binance and
  // IBKR) — try the provider-prefixed registry key first, then the raw code.
  return errorMessages.resolve(`${provider.toUpperCase()}_${code}`) ?? errorMessages.resolve(code);
}

export function connectComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  const accountsStore = inject(AccountsStore, {optional: true});

  return {
    isModalOpen: computed(() => store.modalStep() !== 'closed'),
    isBusy: computed(() => {
      const s = store.status();
      return s === 'initializing' || s === 'syncing' || s === 'polling';
    }),
    isInitializing: computed(() => store.status() === 'initializing'),
    isReady: computed(() => store.status() === 'ready'),
    errorMessage: computed(() => {
      if (store.status() !== 'error') {
        return '';
      }
      return mapErrorByProvider(
        store.errorCode(),
        store.selectedProvider(),
        resolveForProvider(errorMessages, store.errorCode(), store.selectedProvider())
      );
    }),
    connectedProviders: computed<ReadonlySet<Provider>>(() => {
      const summary = accountsStore?.summary();
      if (!summary) {
        return new Set();
      }
      const known = new Set(PROVIDER_SLUGS as readonly string[]);
      const set = new Set<Provider>();
      for (const category of summary.categories) {
        for (const inst of category.institutions) {
          if (known.has(inst.provider)) {
            set.add(inst.provider as Provider);
          }
        }
      }
      return set;
    }),
  };
}
