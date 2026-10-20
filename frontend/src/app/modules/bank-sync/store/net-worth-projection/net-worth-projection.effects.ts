import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type NetWorthProjection} from '../../models/projection/projection.model';
import {WealthService} from '../../services/wealth.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setProjection: (projection: NetWorthProjection) => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function netWorthProjectionEffects(store: EffectsStore) {
  const wealthService = inject(WealthService);

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          wealthService.getNetWorthProjection().pipe(
            tap(projection => {
              store.setProjection(projection);
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

export function netWorthProjectionHooks(store: HookStore): void {
  store.load();
}
