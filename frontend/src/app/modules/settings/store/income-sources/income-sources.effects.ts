import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {
  type IncomeSource,
  type IncomeSourceInput,
} from '../../models/income-source/income-source.model';
import {IncomeSourcesService} from '../../services/income-sources.service';

interface EffectsStore {
  setSourcesLoading: () => void;
  setSourcesSuccess: () => void;
  setSourcesError: (errorCode: Nullable<string>) => void;
  setSaveLoading: () => void;
  setSaveSuccess: () => void;
  setSaveError: (errorCode: Nullable<string>) => void;
  setSources: (sources: IncomeSource[]) => void;
  setBusy: (id: string) => void;
  setIdle: () => void;
  setActionError: (errorCode: Nullable<string>) => void;
  clearActionError: () => void;
}

export function incomeSourcesEffects(store: EffectsStore) {
  const service = inject(IncomeSourcesService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setSourcesLoading()),
      switchMap(() =>
        service.list().pipe(
          tap(sources => {
            store.setSources(sources);
            store.setSourcesSuccess();
          }),
          catchError((err: unknown) => {
            store.setSourcesError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    save: rxMethod<{id: Nullable<string>; input: IncomeSourceInput}>(
      pipe(
        tap(() => {
          store.setSaveLoading();
          store.clearActionError();
        }),
        exhaustMap(({id, input}) =>
          (id ? service.update(id, input) : service.create(input)).pipe(
            tap(() => {
              store.setSaveSuccess();
              load();
            }),
            catchError((err: unknown) => {
              store.setSaveError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
    retire: rxMethod<string>(
      pipe(
        tap(id => store.setBusy(id)),
        exhaustMap(id =>
          service.retire(id).pipe(
            tap(() => {
              store.setIdle();
              load();
            }),
            catchError((err: unknown) => {
              store.setActionError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
    remove: rxMethod<string>(
      pipe(
        tap(id => store.setBusy(id)),
        exhaustMap(id =>
          service.remove(id).pipe(
            tap(() => {
              store.setIdle();
              load();
            }),
            catchError((err: unknown) => {
              store.setActionError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
  };
}

interface HookStore {
  load: () => void;
}

export function incomeSourcesHooks(store: HookStore): void {
  store.load();
}
