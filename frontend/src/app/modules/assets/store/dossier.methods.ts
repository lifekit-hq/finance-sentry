import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type AssetDossierDto, type AssetLedgerReadDto} from '../models/dossier/dossier.model';
import {type DossierState} from './dossier.state';

export function dossierMethods(store: WritableStateSource<DossierState>) {
  return {
    setDossierLoading(): void {
      patchState(store, {dossierStatus: 'loading', dossierErrorCode: null});
    },
    setDossier(dossier: AssetDossierDto): void {
      patchState(store, {dossier, dossierStatus: 'idle', dossierErrorCode: null});
    },
    setDossierError(errorCode: Nullable<string>): void {
      patchState(store, {dossierStatus: 'error', dossierErrorCode: errorCode});
    },
    setLedgerReadLoading(): void {
      patchState(store, {ledgerReadStatus: 'loading', ledgerReadErrorCode: null});
    },
    setLedgerRead(ledgerRead: AssetLedgerReadDto): void {
      patchState(store, {ledgerRead, ledgerReadStatus: 'idle', ledgerReadErrorCode: null});
    },
    toggleThesisExpanded(): void {
      patchState(store, state => ({isThesisExpanded: !state.isThesisExpanded}));
    },
    setLedgerReadError(errorCode: Nullable<string>): void {
      patchState(store, {ledgerReadStatus: 'error', ledgerReadErrorCode: errorCode});
    },
  };
}
