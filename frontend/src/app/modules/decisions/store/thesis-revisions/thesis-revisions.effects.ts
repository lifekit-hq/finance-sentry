import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, filter, pipe, switchMap, tap} from 'rxjs';

import {type ThesisDto} from '../../../../shared/models/thesis/thesis.model';
import {
  type DecideThesisRevisionResult,
  type ThesisRevisionDto,
  type ThesisRevisionsResult,
  type ThesisRevisionVerdict,
} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {ThesisService} from '../../services/thesis.service';
import {ThesisRevisionService} from '../../services/thesis-revision.service';
import {ThesisRevisionSheetService} from '../../services/thesis-revision-sheet.service';

interface EffectsStore {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setData(result: ThesisRevisionsResult): void;
  setTheses(theses: ThesisDto[]): void;
  applyDecision(result: DecideThesisRevisionResult): void;
}

export function thesisRevisionsEffects(store: EffectsStore) {
  const service = inject(ThesisRevisionService);
  const theses = inject(ThesisService);
  const sheet = inject(ThesisRevisionSheetService);

  /** A failed read leaves the cards without a "before"; the proposal and its decision still stand. */
  const loadTheses = rxMethod<void>(
    pipe(
      switchMap(() =>
        theses.list().pipe(
          tap(list => store.setTheses(list)),
          catchError(() => EMPTY)
        )
      )
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
              loadTheses();
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
    decide: rxMethod<{revision: ThesisRevisionDto; verdict: ThesisRevisionVerdict}>(
      pipe(
        exhaustMap(({revision, verdict}) => sheet.open(revision, verdict)),
        filter((decided): decided is DecideThesisRevisionResult => decided !== undefined),
        tap(decided => {
          store.applyDecision(decided);
          if (decided.thesis) {
            loadTheses();
          }
        })
      )
    ),
  };
}

export function thesisRevisionsHooks(store: {load: () => void}): void {
  store.load();
}
