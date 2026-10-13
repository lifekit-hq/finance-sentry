import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type AsyncStateStatus} from '@lifekit-hq/ui';

import {SECTION_AXIS} from '../constants/dossier/dossier-checks.constants';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
} from '../models/dossier/dossier.model';
import {type CheckAxisView} from '../models/dossier/dossier-view.model';
import {DossierChartUtils} from '../utils/dossier-chart.utils';
import {DossierChecksUtils} from '../utils/dossier-checks.utils';
import {DossierFactsUtils} from '../utils/dossier-facts.utils';
import {DossierPositionUtils} from '../utils/dossier-position.utils';
import {DossierQuoteUtils} from '../utils/dossier-quote.utils';
import {DossierViewUtils} from '../utils/dossier-view.utils';

interface StateSignals {
  dossier: Signal<Nullable<AssetDossierDto>>;
  ledgerRead: Signal<Nullable<AssetLedgerReadDto>>;
  isThesisExpanded: Signal<boolean>;
  quote: Signal<Nullable<DossierQuoteDto>>;
  chart: Signal<Nullable<AssetChartDto>>;
  chartStatus: Signal<AsyncStatus>;
  chartErrorMessage: Signal<string>;
  targetDraft: Signal<string>;
}

const PARAGRAPH_BREAK = /\n\s*\n/;

export function dossierComputed(store: StateSignals) {
  const thesisParagraphs = computed(() => {
    const text = store.dossier()?.thesis?.thesisText ?? '';
    return text.split(PARAGRAPH_BREAK).filter(p => p.trim().length > 0);
  });

  const checkAxes = computed(() => DossierChecksUtils.axes(store.dossier()?.checks ?? null));

  // The best price on hand for "Is it cheap?": today's quote, else the valuation's, else the last close.
  const currentPrice = computed((): Nullable<number> => {
    const dossier = store.dossier();
    return (
      store.quote()?.price ?? dossier?.valuation?.price ?? dossier?.performance?.lastClose ?? null
    );
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
        dossier.radarSignals.length > 0 ||
        (dossier.performance ?? null) !== null
      );
    }),
    // Crypto carries no valuation, analyst or ownership data: those sections are hidden, not mocked.
    isCrypto: computed(() => store.dossier()?.valuation?.notApplicable === true),
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

    performanceStrip: computed(() =>
      DossierFactsUtils.performanceStrip(store.dossier()?.performance)
    ),
    thesisHook: computed(() => DossierFactsUtils.thesisHook(store.dossier())),
    sizeCap: computed(() => DossierPositionUtils.sizeCap(store.dossier()?.position ?? null)),
    returnsView: computed(() => DossierPositionUtils.returns(store.dossier()?.position?.returns)),

    checkAxes,
    wheel: computed(() => DossierChecksUtils.wheel(store.dossier()?.checks ?? null)),
    checksPassed: computed(() => checkAxes().reduce((sum, axis) => sum + axis.passed, 0)),
    // Every named rule counts, so the header agrees with the axes' n/k strips; a rule the data could
    // not decide stays unticked rather than dropping out of the count.
    checksTotal: computed(() => checkAxes().reduce((sum, axis) => sum + axis.total, 0)),
    // Each question-led section carries the tick strip of the axis that answers it.
    sectionChecks: computed(() => {
      const byKey = new Map(checkAxes().map(axis => [axis.key, axis]));
      const result: Record<string, Nullable<CheckAxisView>> = {};
      for (const [section, axisKey] of Object.entries(SECTION_AXIS)) {
        result[section] = byKey.get(axisKey) ?? null;
      }
      return result;
    }),

    chartView: computed(() => DossierChartUtils.view(store.chart())),
    chartAsyncStatus: computed((): AsyncStateStatus => {
      if (store.chartErrorMessage() !== '' && store.chartStatus() === 'error') {
        return 'error';
      }
      return store.chartStatus() === 'loading' || store.chartStatus() === 'idle'
        ? 'loading'
        : 'success';
    }),

    currentPrice,
    targetBand: computed(() => {
      const dossier = store.dossier();
      return DossierPositionUtils.targetBand(
        dossier?.valuation ?? null,
        dossier?.userTarget,
        currentPrice()
      );
    }),
    parsedTargetDraft: computed(() => DossierPositionUtils.parseTarget(store.targetDraft())),
    fundamentals: computed(() => {
      const dossier = store.dossier();
      return DossierFactsUtils.fundamentals(dossier?.valuation ?? null, dossier?.performance);
    }),
    dataStatus: computed(() => DossierFactsUtils.dataStatus(store.dossier(), store.chart())),
  };
}
