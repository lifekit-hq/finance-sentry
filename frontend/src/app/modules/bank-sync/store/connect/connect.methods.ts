import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type PickableProvider,
  type Provider,
} from '../../../../shared/models/provider/provider.model';
import {type InstitutionType} from '../../../../shared/models/provider/provider.model';
import {type ModalStep} from '../../models/connect/connect.model';
import {type ConnectState} from './connect.state';

const STEP_FOR_TYPE: Record<InstitutionType, ModalStep> = {
  bank: 'provider-picker',
  crypto: 'provider-picker',
  broker: 'ibkr-form',
};

const STEP_FOR_PICKED_PROVIDER: Record<PickableProvider, ModalStep> = {
  monobank: 'monobank-form',
  truelayer: 'truelayer-picker',
  binance: 'binance-form',
  // Provider slugs are the backend's snake_case wire values.
  // eslint-disable-next-line @typescript-eslint/naming-convention
  revolut_x: 'revolut-x-form',
};

const TYPE_FOR_PICKED_PROVIDER: Record<PickableProvider, InstitutionType> = {
  monobank: 'bank',
  truelayer: 'bank',
  binance: 'crypto',
  // eslint-disable-next-line @typescript-eslint/naming-convention
  revolut_x: 'crypto',
};

// Types with a single provider skip the picker.
const PROVIDER_FOR_TYPE: Partial<Record<InstitutionType, Provider>> = {
  broker: 'ibkr',
};

export function connectMethods(store: WritableStateSource<ConnectState>) {
  return {
    openModal(): void {
      patchState(store, {
        modalStep: 'type-picker',
        status: 'idle',
        errorCode: null,
        statusMessage: null,
        institutionType: null,
        selectedProvider: 'truelayer',
      });
    },
    closeModal(): void {
      patchState(store, {
        modalStep: 'closed',
        status: 'idle',
        errorCode: null,
        statusMessage: null,
        institutionType: null,
      });
    },
    selectInstitutionType(type: InstitutionType): void {
      const provider = PROVIDER_FOR_TYPE[type];
      patchState(store, {
        institutionType: type,
        modalStep: STEP_FOR_TYPE[type],
        status: 'idle',
        errorCode: null,
        statusMessage: null,
        ...(provider ? {selectedProvider: provider} : {}),
      });
    },
    setInstitutionType(type: InstitutionType): void {
      patchState(store, {institutionType: type});
    },
    setModalStep(step: ModalStep): void {
      patchState(store, {modalStep: step, status: 'idle', errorCode: null, statusMessage: null});
    },
    selectProvider(provider: Provider): void {
      patchState(store, {
        selectedProvider: provider,
        status: 'idle',
        errorCode: null,
        statusMessage: null,
      });
    },
    selectPickedProvider(slug: PickableProvider): void {
      patchState(store, {
        selectedProvider: slug,
        institutionType: TYPE_FOR_PICKED_PROVIDER[slug],
        modalStep: STEP_FOR_PICKED_PROVIDER[slug],
        status: 'idle',
        errorCode: null,
        statusMessage: null,
      });
    },
    setInitializing(): void {
      patchState(store, {status: 'initializing', errorCode: null, statusMessage: null});
    },
    setReady(): void {
      patchState(store, {status: 'ready', errorCode: null});
    },
    setSyncing(message: string): void {
      patchState(store, {status: 'syncing', statusMessage: message, errorCode: null});
    },
    setPolling(message: string): void {
      patchState(store, {status: 'polling', statusMessage: message});
    },
    setSuccess(): void {
      patchState(store, {
        status: 'success',
        statusMessage: null,
        errorCode: null,
        modalStep: 'closed',
      });
    },
    setError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode, statusMessage: null});
    },
    resetError(): void {
      patchState(store, {status: 'idle', errorCode: null, statusMessage: null});
    },
    clearStatus(): void {
      patchState(store, {statusMessage: null});
    },
  };
}
