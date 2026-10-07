import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';
import {DossierQuoteUtils} from '../utils/dossier-quote.utils';
import {type DossierState} from './dossier.state';

interface StateSignals {
  dossier: Signal<Nullable<AssetDossierDto>>;
  dossierStatus: Signal<DossierState['dossierStatus']>;
  dossierErrorCode: Signal<Nullable<string>>;
  ledgerRead: Signal<Nullable<AssetLedgerReadDto>>;
  ledgerReadStatus: Signal<DossierState['ledgerReadStatus']>;
  ledgerReadErrorCode: Signal<Nullable<string>>;
  isThesisExpanded: Signal<boolean>;
  quote: Signal<Nullable<DossierQuoteDto>>;
  quoteStatus: Signal<DossierState['quoteStatus']>;
}

const PARAGRAPH_BREAK = /\n\s*\n/;
const DEFAULT_DOSSIER_ERROR = 'Failed to load asset dossier.';
const DEFAULT_LEDGER_READ_ERROR = "Failed to load Ledger's read.";

export function dossierComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  const thesisParagraphs = computed(() => {
    const text = store.dossier()?.thesis?.thesisText ?? '';
    return text.split(PARAGRAPH_BREAK).filter(p => p.trim().length > 0);
  });

  return {
    isDossierLoading: computed(() => store.dossierStatus() === 'loading'),
    dossierErrorMessage: computed(() => {
      if (store.dossierStatus() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.dossierErrorCode()) ?? DEFAULT_DOSSIER_ERROR;
    }),
    // Mirrors the render conditions in the template: a dossier whose every section is
    // null/empty renders a "no data" state instead of a page of hidden cards.
    hasDossierSections: computed(() => {
      const dossier = store.dossier();
      if (!dossier) {
        return false;
      }
      return (
        dossier.position !== null ||
        dossier.thesis !== null ||
        (dossier.valuation !== null && !dossier.valuation.notApplicable) ||
        dossier.analysts !== null ||
        dossier.nextEarnings !== null ||
        dossier.recentNews.length > 0 ||
        dossier.radarSignals.length > 0
      );
    }),
    // The thesis is a wall of text on a phone: show the first paragraph, the rest behind "Read more".
    thesisParagraphs,
    thesisHasMore: computed(() => thesisParagraphs().length > 1),
    visibleThesisParagraphs: computed(() =>
      store.isThesisExpanded() ? thesisParagraphs() : thesisParagraphs().slice(0, 1)
    ),
    isQuoteLoading: computed(() => store.quoteStatus() === 'loading'),
    quoteHeader: computed(() => DossierQuoteUtils.toHeader(store.quote())),
    isLedgerReadLoading: computed(() => store.ledgerReadStatus() === 'loading'),
    ledgerReadNarrative: computed(() => store.ledgerRead()?.narrative ?? ''),
    // The backend reports a missing cache as stale; only an actual narrative can be out of date.
    isLedgerReadStale: computed(() => {
      const read = store.ledgerRead();
      return read?.isStale === true && !!read.narrative;
    }),
    ledgerReadErrorMessage: computed(() => {
      if (store.ledgerReadStatus() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.ledgerReadErrorCode()) ?? DEFAULT_LEDGER_READ_ERROR;
    }),
  };
}
