import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type ChartRangeId,
  type DossierQuoteDto,
  type DossierUserTargetDto,
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
    setChart(chart: AssetChartDto): void {
      patchState(store, {chart});
    },
    setChartRange(chartRange: ChartRangeId): void {
      patchState(store, {chartRange});
    },
    setTargetDraft(targetDraft: string): void {
      patchState(store, {targetDraft});
    },
    // The editor opens on the saved target, so a change starts from what is there.
    openTargetEditor(): void {
      patchState(store, state => ({
        isEditingTarget: true,
        targetDraft: state.dossier?.userTarget ? String(state.dossier.userTarget.targetPrice) : '',
      }));
    },
    closeTargetEditor(): void {
      patchState(store, {isEditingTarget: false, targetDraft: ''});
    },
    // Saved or cleared on the server: the dossier carries the new target and the editor closes.
    setUserTarget(userTarget: Nullable<DossierUserTargetDto>): void {
      patchState(store, state => ({
        dossier: state.dossier ? {...state.dossier, userTarget} : null,
        isEditingTarget: false,
        targetDraft: '',
      }));
    },
  };
}
