import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, filter, pipe, switchMap, tap} from 'rxjs';

import {
  type RecommendationDto,
  type RecommendationsResult,
  type RecommendationVerdict,
} from '../../../../shared/models/recommendation/recommendation.model';
import {DecisionSheetService} from '../../services/decision-sheet.service';
import {RecommendationService} from '../../services/recommendation.service';

interface EffectsStore {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setData(result: RecommendationsResult): void;
  applyDecision(decided: RecommendationDto): void;
}

export function decisionsEffects(store: EffectsStore) {
  const service = inject(RecommendationService);
  const sheet = inject(DecisionSheetService);

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          service.list().pipe(
            tap(result => {
              store.setData(result);
              store.setSuccess();
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

export function decisionsHooks(store: {load: () => void}): void {
  store.load();
}
