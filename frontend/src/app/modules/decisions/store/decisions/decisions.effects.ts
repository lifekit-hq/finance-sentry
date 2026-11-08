import {inject, type Signal} from '@angular/core';
import {ActivatedRoute} from '@angular/router';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, filter, from, mergeMap, pipe, switchMap, tap} from 'rxjs';

import {LEDGER_DECISION_ID_PARAM} from '../../../../shared/enums/app-route/app-route.enum';
import {
  type RecommendationDto,
  type RecommendationsResult,
  type RecommendationVerdict,
} from '../../../../shared/models/recommendation/recommendation.model';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';
import {DecisionSheetService} from '../../services/decision-sheet.service';
import {RecommendationService} from '../../services/recommendation.service';
import {ScorecardService} from '../../services/scorecard.service';

interface EffectsStore {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setData(result: RecommendationsResult): void;
  applyDecision(decided: RecommendationDto): void;
  select(id: Nullable<string>): void;
  selectedId: Signal<Nullable<string>>;
  setScorecard(card: ScorecardDto): void;
}

/** Scorecard reads in flight at once; a long queue does not fire every read together. */
const SCORECARD_CONCURRENCY = 4;

export function decisionsEffects(store: EffectsStore) {
  const service = inject(RecommendationService);
  const sheet = inject(DecisionSheetService);
  const scorecards = inject(ScorecardService);

  /** The open proposal's ticker verdict (spec 726 US2); a failed read leaves the detail without one. */
  const loadScorecards = rxMethod<string[]>(
    pipe(
      switchMap(tickers =>
        from(tickers).pipe(
          mergeMap(
            ticker => scorecards.read(ticker).pipe(catchError(() => EMPTY)),
            SCORECARD_CONCURRENCY
          )
        )
      ),
      tap(card => store.setScorecard(card))
    )
  );

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          service.list().pipe(
            tap(result => {
              store.setData(result);
              store.setSuccess();
              const open = result.pending.find(rec => rec.id === store.selectedId());
              if (open) {
                loadScorecards([open.ticker]);
              }
            }),
            catchError((err: unknown) => {
              store.setError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
    /** Opens the Confirm or Reject sheet; a recorded verdict moves the proposal to the decided list. */
    decide: rxMethod<{recommendation: RecommendationDto; verdict: RecommendationVerdict}>(
      pipe(
        exhaustMap(({recommendation, verdict}) => sheet.open(recommendation, verdict)),
        filter((decided): decided is RecommendationDto => decided !== undefined),
        tap(decided => store.applyDecision(decided))
      )
    ),
  };
}

/** The queue opens on no proposal; a detail route names one by id. */
export function decisionsHooks(store: {
  load: () => void;
  select: (id: Nullable<string>) => void;
}): void {
  store.select(inject(ActivatedRoute).snapshot.paramMap.get(LEDGER_DECISION_ID_PARAM));
  store.load();
}
