import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type FireProjection} from '../../models/fire/fire.model';
import {WealthService} from '../../services/wealth.service';

interface EffectsStore {
  setLoading: () => void;
  setProjection: (projection: FireProjection) => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function fireEffects(store: EffectsStore) {
  const wealthService = inject(WealthService);

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          wealthService.getFireProjection().pipe(
            tap(projection => store.setProjection(projection)),
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

export function fireHooks(store: HookStore): void {
  store.load();
}
