import {computed, type Signal} from '@angular/core';

import {
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';
import {DossierQuoteUtils} from '../utils/dossier-quote.utils';
import {DossierViewUtils} from '../utils/dossier-view.utils';

interface StateSignals {
  dossier: Signal<Nullable<AssetDossierDto>>;
  ledgerRead: Signal<Nullable<AssetLedgerReadDto>>;
  isThesisExpanded: Signal<boolean>;
  quote: Signal<Nullable<DossierQuoteDto>>;
}

const PARAGRAPH_BREAK = /\n\s*\n/;

export function dossierComputed(store: StateSignals) {
  const thesisParagraphs = computed(() => {
    const text = store.dossier()?.thesis?.thesisText ?? '';
    return text.split(PARAGRAPH_BREAK).filter(p => p.trim().length > 0);
  });

  return {
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
    quoteHeader: computed(() => DossierQuoteUtils.toHeader(store.quote())),
    heroProfile: computed(() => {
      const dossier = store.dossier();
      return dossier ? DossierViewUtils.heroProfile(dossier) : null;
    }),
    sectionChips: computed(() => DossierViewUtils.sectionChips(store.dossier())),
    // The latest month of the street's ratings as one bar and one verdict ("21 of 26 Buy").
    recommendationSummary: computed(() =>
      DossierViewUtils.recommendationSummary(store.dossier()?.analysts?.trends ?? [])
    ),
    ledgerReadNarrative: computed(() => store.ledgerRead()?.narrative ?? ''),
    // The backend reports a missing cache as stale; only an actual narrative can be out of date.
    isLedgerReadStale: computed(() => {
      const read = store.ledgerRead();
      return read?.isStale === true && !!read.narrative;
    }),
  };
}
