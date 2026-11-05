import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type GoalsProgress} from '../../models/goals-progress/goals-progress.model';
import {WealthService} from '../../services/wealth.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setProgress: (progress: GoalsProgress) => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function goalsProgressEffects(store: EffectsStore) {
  const wealthService = inject(WealthService);

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          wealthService.getGoalsProgress().pipe(
            tap(progress => {
              store.setProgress(progress);
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
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
}

export function goalsProgressHooks(store: HookStore): void {
  store.load();
}
