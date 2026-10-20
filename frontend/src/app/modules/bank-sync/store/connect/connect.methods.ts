import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type PickableProvider,
  type Provider,
} from '../../../../shared/models/provider/provider.model';
import {type InstitutionType} from '../../../../shared/models/provider/provider.model';
import {type ModalStep} from '../../models/connect/connect.model';
import {type ConnectState} from './connect.state';

interface StatusSetters {
  setConnectIdle(): void;
  setConnectLoading(): void;
  setConnectSuccess(): void;
  setConnectError(errorCode: Nullable<string>): void;
}

const STEP_FOR_TYPE: Record<InstitutionType, ModalStep> = {
  bank: 'provider-picker',
  crypto: 'provider-picker',
  broker: 'provider-picker',
};

const STEP_FOR_PICKED_PROVIDER: Record<PickableProvider, ModalStep> = {
  monobank: 'monobank-form',
  truelayer: 'truelayer-picker',
  binance: 'binance-form',
  // Provider slugs are the backend's snake_case wire values.
  // eslint-disable-next-line @typescript-eslint/naming-convention
  revolut_x: 'revolut-x-form',
  ibkr: 'ibkr-form',
  inzhur: 'inzhur-form',
};

const TYPE_FOR_PICKED_PROVIDER: Record<PickableProvider, InstitutionType> = {
  monobank: 'bank',
  truelayer: 'bank',
  binance: 'crypto',
  // eslint-disable-next-line @typescript-eslint/naming-convention
  revolut_x: 'crypto',
  ibkr: 'broker',
  inzhur: 'broker',
};

export function connectMethods(store: StatusSetters & WritableStateSource<ConnectState>) {
  return {
    openModal(): void {
      patchState(store, {
        modalStep: 'type-picker',
        statusMessage: null,
        institutionType: null,
        importPending: false,
        selectedProvider: 'truelayer',
      });
      store.setConnectIdle();
    },
    closeModal(): void {
      patchState(store, {modalStep: 'closed', statusMessage: null, institutionType: null});
      store.setConnectIdle();
    },
    selectInstitutionType(type: InstitutionType): void {
      patchState(store, {
        institutionType: type,
        modalStep: STEP_FOR_TYPE[type],
        statusMessage: null,
      });
      store.setConnectIdle();
    },
    setInstitutionType(type: InstitutionType): void {
      patchState(store, {institutionType: type});
    },
    setModalStep(step: ModalStep): void {
      patchState(store, {modalStep: step, statusMessage: null});
      store.setConnectIdle();
    },
    selectProvider(provider: Provider): void {
      patchState(store, {selectedProvider: provider, statusMessage: null});
      store.setConnectIdle();
    },
    selectPickedProvider(slug: PickableProvider): void {
      patchState(store, {
        selectedProvider: slug,
        institutionType: TYPE_FOR_PICKED_PROVIDER[slug],
        modalStep: STEP_FOR_PICKED_PROVIDER[slug],
        statusMessage: null,
      });
      store.setConnectIdle();
    },
    setSyncing(message: string): void {
      patchState(store, {statusMessage: message});
      store.setConnectLoading();
    },
    setPolling(message: string): void {
      patchState(store, {statusMessage: message});
      store.setConnectLoading();
    },
    setSuccess(importPending = false): void {
      patchState(store, {importPending, statusMessage: null, modalStep: 'closed'});
      store.setConnectSuccess();
    },
    setError(errorCode: Nullable<string>): void {
      patchState(store, {statusMessage: null});
      store.setConnectError(errorCode);
    },
    resetError(): void {
      patchState(store, {statusMessage: null});
      store.setConnectIdle();
    },
    clearStatus(): void {
      patchState(store, {statusMessage: null});
    },
  };
}
