import {DOCUMENT} from '@angular/common';
import {afterNextRender, inject, Injector, type Signal} from '@angular/core';
import {ActivatedRoute} from '@angular/router';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, filter, pipe, switchMap, tap} from 'rxjs';

import {ASSET_DOSSIER_SYMBOL_PARAM} from '../../../shared/enums/app-route/app-route.enum';
import {
  type RecommendationDto,
  type RecommendationVerdict,
} from '../../../shared/models/recommendation/recommendation.model';
import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {AuthStore} from '../../auth/store/auth.store';
import {DecisionSheetService} from '../../decisions/services/decision-sheet.service';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type ChartRangeId,
  type DossierQuoteDto,
  type DossierUserTargetDto,
} from '../models/dossier/dossier.model';
import {DossierService} from '../services/dossier.service';
import {DossierQuoteUtils} from '../utils/dossier-quote.utils';

interface StoreMethods {
  setDossierLoading(): void;
  setDossierSuccess(): void;
  setDossier(dossier: AssetDossierDto): void;
  setDossierError(errorCode: Nullable<string>): void;
  setLedgerReadLoading(): void;
  setLedgerReadSuccess(): void;
  setLedgerRead(ledgerRead: AssetLedgerReadDto): void;
  setLedgerReadError(errorCode: Nullable<string>): void;
  setQuoteLoading(): void;
  setQuoteSuccess(): void;
  setQuote(quote: Nullable<DossierQuoteDto>): void;
  setQuoteError(errorCode: Nullable<string>): void;
  clearQuote(): void;
  setChartLoading(): void;
  setChartSuccess(): void;
  setChart(chart: AssetChartDto): void;
  setChartError(errorCode: Nullable<string>): void;
  setChartRange(range: ChartRangeId): void;
  setTargetLoading(): void;
  setTargetSuccess(): void;
  setTargetError(errorCode: Nullable<string>): void;
  setUserTarget(target: Nullable<DossierUserTargetDto>): void;
  applyRecommendationDecision(decided: RecommendationDto): void;
  dossier: Signal<Nullable<AssetDossierDto>>;
  chartRange: Signal<ChartRangeId>;
  parsedTargetDraft: Signal<Nullable<number>>;
}

export function dossierEffects(store: StoreMethods) {
  const dossierService = inject(DossierService);
  const route = inject(ActivatedRoute);
  const document = inject(DOCUMENT);
  const injector = inject(Injector);
  const decisionSheet = inject(DecisionSheetService);

  // An alert deep-links to a card (`/assets/NVDA#analyst-coverage`); the card only exists once the
  // dossier has loaded, so the router's own fragment handling has nothing to land on. Scroll after
  // the render that follows the dossier arriving.
  const scrollToFragment = () => {
    const fragment = route.snapshot.fragment;
    if (!fragment) {
      return;
    }
    afterNextRender(() => document.getElementById(fragment)?.scrollIntoView({block: 'start'}), {
      injector,
    });
  };

  // Runs once the dossier says the symbol is a listed equity; a failed quote never breaks the page.
  const loadQuote = rxMethod<string>(
    pipe(
      tap(() => store.setQuoteLoading()),
      switchMap(symbol =>
        dossierService.getQuotes(symbol).pipe(
          tap(quotes => {
            store.setQuote(DossierQuoteUtils.toMajorUnits(quotes[0] ?? null));
            store.setQuoteSuccess();
          }),
          catchError(() => {
            store.clearQuote();
            store.setQuoteError(null);
            return EMPTY;
          })
        )
      )
    )
  );

  // A failed chart keeps the rest of the page; switchMap drops a range the user already moved past.
  const loadChart = rxMethod<{symbol: string; range: ChartRangeId}>(
    pipe(
      tap(() => store.setChartLoading()),
      switchMap(({symbol, range}) =>
        dossierService.getChart(symbol, range).pipe(
          tap(chart => {
            store.setChart(chart);
            store.setChartSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setChartError(code),
          })
        )
      )
    )
  );

  const selectChartRange = (range: ChartRangeId): void => {
    const symbol = store.dossier()?.symbol;
    if (!symbol || range === store.chartRange()) {
      return;
    }
    store.setChartRange(range);
    loadChart({symbol, range});
  };

  const putTarget = rxMethod<{symbol: string; targetPrice: number; currency: Nullable<string>}>(
    pipe(
      tap(() => store.setTargetLoading()),
      switchMap(({symbol, targetPrice, currency}) =>
        dossierService.setTarget(symbol, {targetPrice, currency}).pipe(
          tap(target => {
            store.setUserTarget(target);
            store.setTargetSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setTargetError(code),
          })
        )
      )
    )
  );

  // Saves the editor's draft in the price's own currency; an invalid draft never leaves the page.
  const saveTarget = (): void => {
    const dossier = store.dossier();
    const targetPrice = store.parsedTargetDraft();
    if (!dossier || targetPrice === null) {
      return;
    }
    const currency = dossier.valuation?.currency ?? dossier.performance?.currency ?? null;
    putTarget({symbol: dossier.symbol, targetPrice, currency});
  };

  const deleteTarget = rxMethod<string>(
    pipe(
      tap(() => store.setTargetLoading()),
      switchMap(symbol =>
        dossierService.clearTarget(symbol).pipe(
          tap(() => {
            store.setUserTarget(null);
            store.setTargetSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setTargetError(code),
          })
        )
      )
    )
  );

  const clearTarget = (): void => {
    const symbol = store.dossier()?.symbol;
    if (symbol) {
      deleteTarget(symbol);
    }
  };

  const loadDossier = rxMethod<string>(
    pipe(
      tap(() => store.setDossierLoading()),
      switchMap(symbol =>
        dossierService.getDossier(symbol).pipe(
          tap(dossier => {
            store.setDossier(dossier);
            store.setDossierSuccess();
            if (DossierQuoteUtils.isQuotable(dossier)) {
              loadQuote(dossier.symbol);
            }
            loadChart({symbol: dossier.symbol, range: store.chartRange()});
            scrollToFragment();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setDossierError(code),
          })
        )
      )
    )
  );

  // Cached-only fetch — runs on page load so a previously generated read renders instantly.
  const loadLedgerRead = rxMethod<string>(
    pipe(
      tap(() => store.setLedgerReadLoading()),
      switchMap(symbol =>
        dossierService.getLedgerRead(symbol).pipe(
          tap(read => {
            store.setLedgerRead(read);
            store.setLedgerReadSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setLedgerReadError(code),
          })
        )
      )
    )
  );

  const generateLedgerRead = rxMethod<{symbol: string; force: boolean}>(
    pipe(
      tap(() => store.setLedgerReadLoading()),
      switchMap(({symbol, force}) =>
        dossierService.generateLedgerRead(symbol, force).pipe(
          tap(read => {
            store.setLedgerRead(read);
            store.setLedgerReadSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setLedgerReadError(code),
          })
        )
      )
    )
  );

  /** Opens the Confirm or Reject sheet for the pending proposal; a recorded verdict replaces the card. */
  const decideRecommendation = rxMethod<{
    recommendation: RecommendationDto;
    verdict: RecommendationVerdict;
  }>(
    pipe(
      exhaustMap(({recommendation, verdict}) => decisionSheet.open(recommendation, verdict)),
      filter((decided): decided is RecommendationDto => decided !== undefined),
      tap(decided => store.applyRecommendationDecision(decided))
    )
  );

  return {
    loadDossier,
    decideRecommendation,
    loadLedgerRead,
    generateLedgerRead,
    loadChart,
    selectChartRange,
    saveTarget,
    clearTarget,
  };
}

export function dossierHooks(store: ReturnType<typeof dossierEffects>) {
  const route = inject(ActivatedRoute);
  const authStore = inject(AuthStore);

  return {
    onInit: () => {
      const symbol = route.snapshot.paramMap.get(ASSET_DOSSIER_SYMBOL_PARAM) ?? '';
      if (symbol) {
        store.loadDossier(symbol);
        if (authStore.canUseAi()) {
          store.loadLedgerRead(symbol);
        }
      }
    },
  };
}
