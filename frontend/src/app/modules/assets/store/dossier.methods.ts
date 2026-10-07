import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';
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
    setQuoteLoading(): void {
      patchState(store, {quoteStatus: 'loading'});
    },
    setQuote(quote: Nullable<DossierQuoteDto>): void {
      patchState(store, {quote, quoteStatus: 'idle'});
    },
    // The header price is a nicety: a failed quote leaves the slot empty rather than raising an error.
    setQuoteError(): void {
      patchState(store, {quote: null, quoteStatus: 'error'});
    },
    toggleThesisExpanded(): void {
      patchState(store, state => ({isThesisExpanded: !state.isThesisExpanded}));
    },
    setLedgerReadError(errorCode: Nullable<string>): void {
      patchState(store, {ledgerReadStatus: 'error', ledgerReadErrorCode: errorCode});
    },
  };
}
