import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';
import {type DossierState} from './dossier.state';

export function dossierMethods(store: WritableStateSource<DossierState>) {
  return {
    setDossier(dossier: AssetDossierDto): void {
      patchState(store, {dossier});
    },
    setLedgerRead(ledgerRead: AssetLedgerReadDto): void {
      patchState(store, {ledgerRead});
    },
    setQuote(quote: Nullable<DossierQuoteDto>): void {
      patchState(store, {quote});
    },
    // The header price is a nicety: a failed quote leaves the slot empty rather than raising an error.
    clearQuote(): void {
      patchState(store, {quote: null});
    },
    toggleThesisExpanded(): void {
      patchState(store, state => ({isThesisExpanded: !state.isThesisExpanded}));
    },
  };
}
