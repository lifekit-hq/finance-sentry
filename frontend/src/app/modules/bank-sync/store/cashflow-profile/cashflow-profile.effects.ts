import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type CashflowProfile} from '../../models/cashflow-profile/cashflow-profile.model';
import {WealthService} from '../../services/wealth.service';

interface EffectsStore {
  setLoading: () => void;
  setSuccess: () => void;
  setProfile: (profile: CashflowProfile) => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function cashflowProfileEffects(store: EffectsStore) {
  const wealthService = inject(WealthService);

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoading()),
        switchMap(() =>
          wealthService.getCashflowProfile().pipe(
            tap(profile => {
              store.setProfile(profile);
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

export function cashflowProfileHooks(store: HookStore): void {
  store.load();
}
