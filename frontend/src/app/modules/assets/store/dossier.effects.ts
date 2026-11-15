import {DOCUMENT} from '@angular/common';
import {afterNextRender, inject, Injector, type Signal} from '@angular/core';
import {ActivatedRoute, type ParamMap} from '@angular/router';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {
  catchError,
  distinctUntilChanged,
  EMPTY,
  exhaustMap,
  filter,
  map,
  pipe,
  Subject,
  switchMap,
  takeUntil,
  tap,
} from 'rxjs';

import {VERDICT_DISPLAYS} from '../../../shared/constants/preferences/app-preferences.constants';
import {ASSET_DOSSIER_SYMBOL_PARAM} from '../../../shared/enums/app-route/app-route.enum';
import {
  type RecommendationDto,
  type RecommendationVerdict,
} from '../../../shared/models/recommendation/recommendation.model';
import {
  type DecideThesisRevisionResult,
  type ThesisRevisionDto,
  type ThesisRevisionVerdict,
} from '../../../shared/models/thesis-revision/thesis-revision.model';
import {AppPreferencesStore} from '../../../shared/store/preferences/app-preferences.store';
import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {AuthStore} from '../../auth/store/auth.store';
import {DecisionSheetService} from '../../decisions/services/decision-sheet.service';
import {ThesisRevisionService} from '../../decisions/services/thesis-revision.service';
import {ThesisRevisionSheetService} from '../../decisions/services/thesis-revision-sheet.service';
import {CHART_HISTORY_RANGE} from '../constants/dossier/dossier-chart.constants';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
  type DossierUserTargetDto,
} from '../models/dossier/dossier.model';
import {DossierService} from '../services/dossier.service';
import {DossierQuoteUtils} from '../utils/dossier-quote.utils';

interface StoreMethods {
  resetForSymbol(): void;
  setDossierLoading(): void;
  setDossierSuccess(): void;
  setDossier(dossier: AssetDossierDto): void;
  setDossierError(errorCode: Nullable<string>): void;
  setLedgerReadLoading(): void;
  setLedgerReadSuccess(): void;
  setLedgerRead(ledgerRead: AssetLedgerReadDto): void;
  setLedgerReadError(errorCode: Nullable<string>): void;
  setLedgerReadIdle(): void;
  setQuoteLoading(): void;
  setQuoteSuccess(): void;
  setQuote(quote: Nullable<DossierQuoteDto>): void;
  setQuoteError(errorCode: Nullable<string>): void;
  setQuoteIdle(): void;
  clearQuote(): void;
  setChartLoading(): void;
  setChartSuccess(): void;
  setChart(chart: AssetChartDto): void;
  setChartError(errorCode: Nullable<string>): void;
  setChartIdle(): void;
  setTargetLoading(): void;
  setTargetSuccess(): void;
  setTargetError(errorCode: Nullable<string>): void;
  setTargetIdle(): void;
  setUserTarget(target: Nullable<DossierUserTargetDto>): void;
  applyRecommendationDecision(decided: RecommendationDto): void;
  setPendingRevision(revision: Nullable<ThesisRevisionDto>): void;
  applyRevisionDecision(result: DecideThesisRevisionResult): void;
  dossier: Signal<Nullable<AssetDossierDto>>;
  parsedTargetDraft: Signal<Nullable<number>>;
}

export function dossierEffects(store: StoreMethods) {
  const dossierService = inject(DossierService);
  const route = inject(ActivatedRoute);
  const document = inject(DOCUMENT);
  const injector = inject(Injector);
  const decisionSheet = inject(DecisionSheetService);
  const revisions = inject(ThesisRevisionService);
  const revisionSheet = inject(ThesisRevisionSheetService);
  const authStore = inject(AuthStore);
  const preferences = inject(AppPreferencesStore);

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

  // Every per-symbol request ends when the symbol changes, so a late response never lands under the new header.
  const symbolChanged$ = new Subject<void>();

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
          }),
          takeUntil(symbolChanged$)
        )
      )
    )
  );

  // A failed chart keeps the rest of the page. The whole history is read once; the price block cuts it to the period.
  const loadChart = rxMethod<string>(
    pipe(
      tap(() => store.setChartLoading()),
      switchMap(symbol =>
        dossierService.getChart(symbol, CHART_HISTORY_RANGE).pipe(
          tap(chart => {
            store.setChart(chart);
            store.setChartSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setChartError(code),
          }),
          takeUntil(symbolChanged$)
        )
      )
    )
  );

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
          }),
          takeUntil(symbolChanged$)
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
          }),
          takeUntil(symbolChanged$)
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
            loadChart(dossier.symbol);
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
          }),
          takeUntil(symbolChanged$)
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
          }),
          takeUntil(symbolChanged$)
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
      exhaustMap(({recommendation, verdict}) =>
        decisionSheet.open(recommendation, verdict).pipe(takeUntil(symbolChanged$))
      ),
      filter((decided): decided is RecommendationDto => decided !== undefined),
      tap(decided => store.applyRecommendationDecision(decided))
    )
  );

  // Ledger's pending thesis change for this ticker (owner only). The Decisions queue is the surface that reports a
  // failed read; here a failure leaves the page without the card rather than raising an error over the dossier.
  const loadRevision = rxMethod<string>(
    pipe(
      switchMap(symbol =>
        revisions.list().pipe(
          tap(result =>
            store.setPendingRevision(
              result.pending.find(revision => revision.ticker === symbol) ?? null
            )
          ),
          catchError(() => EMPTY),
          takeUntil(symbolChanged$)
        )
      )
    )
  );

  /** Opens the Confirm or Reject sheet for the pending thesis change; a recorded verdict replaces the card. */
  const decideRevision = rxMethod<{revision: ThesisRevisionDto; verdict: ThesisRevisionVerdict}>(
    pipe(
      exhaustMap(({revision, verdict}) =>
        revisionSheet.open(revision, verdict).pipe(takeUntil(symbolChanged$))
      ),
      filter((decided): decided is DecideThesisRevisionResult => decided !== undefined),
      tap(decided => store.applyRevisionDecision(decided))
    )
  );

  // A symbol change in place (palette search from one dossier to another) reuses this component, so
  // the previous symbol's content is dropped before the new one loads.
  const openSymbol = (symbol: string): void => {
    symbolChanged$.next();
    store.resetForSymbol();
    store.setLedgerReadIdle();
    store.setQuoteIdle();
    store.setChartIdle();
    store.setTargetIdle();
    loadDossier(symbol);
    if (authStore.canUseAi()) {
      loadLedgerRead(symbol);
      loadRevision(symbol);
    }
  };

  // The choice follows the person: it is saved to the account, applied at once, and rolled back (with a toast)
  // by the preferences store when the save is refused.
  const setVerdictDisplay = (choice: string): void => {
    const verdictDisplay = VERDICT_DISPLAYS.find(display => display === choice);
    if (verdictDisplay) {
      void preferences.patch({verdictDisplay});
    }
  };

  return {
    openSymbol,
    setVerdictDisplay,
    loadDossier,
    decideRecommendation,
    decideRevision,
    loadLedgerRead,
    generateLedgerRead,
    loadChart,
    saveTarget,
    clearTarget,
  };
}

export function dossierHooks(store: ReturnType<typeof dossierEffects>) {
  const route = inject(ActivatedRoute);

  // Follows the route's paramMap, not its snapshot, so the page tracks the symbol after init too.
  const followSymbol = rxMethod<ParamMap>(
    pipe(
      map(params => params.get(ASSET_DOSSIER_SYMBOL_PARAM) ?? ''),
      filter(symbol => symbol !== ''),
      distinctUntilChanged(),
      tap(symbol => store.openSymbol(symbol))
    )
  );

  return {
    onInit: () => followSymbol(route.paramMap),
  };
}
