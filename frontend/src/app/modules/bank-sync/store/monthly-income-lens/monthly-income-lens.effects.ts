import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type MonthlyIncomeLens} from '../../models/income-lens/income-lens.model';
import {WealthService} from '../../services/wealth.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setLens: (lens: MonthlyIncomeLens) => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function monthlyIncomeLensEffects(store: EffectsStore) {
  const wealthService = inject(WealthService);

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          wealthService.getMonthlyIncomeLens().pipe(
            tap(lens => {
              store.setLens(lens);
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

export function monthlyIncomeLensHooks(store: HookStore): void {
  store.load();
}
