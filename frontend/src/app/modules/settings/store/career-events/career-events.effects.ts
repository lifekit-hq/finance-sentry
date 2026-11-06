import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, switchMap, tap} from 'rxjs';

import {
  type CareerEvent,
  type CareerEventInput,
} from '../../models/career-event/career-event.model';
import {CareerEventsService} from '../../services/career-events.service';

interface EffectsStore {
  setEventsLoading: () => void;
  setEventsSuccess: () => void;
  setEventsError: (errorCode: Nullable<string>) => void;
  setSaveLoading: () => void;
  setSaveSuccess: () => void;
  setSaveError: (errorCode: Nullable<string>) => void;
  setEvents: (events: CareerEvent[]) => void;
  setBusy: (id: string) => void;
  setIdle: () => void;
  setActionError: (errorCode: Nullable<string>) => void;
  clearActionError: () => void;
}

export function careerEventsEffects(store: EffectsStore) {
  const service = inject(CareerEventsService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setEventsLoading()),
      switchMap(() =>
        service.list().pipe(
          tap(events => {
            store.setEvents(events);
            store.setEventsSuccess();
          }),
          catchError((err: unknown) => {
            store.setEventsError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    save: rxMethod<{id: Nullable<string>; input: CareerEventInput}>(
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

export function careerEventsHooks(store: HookStore): void {
  store.load();
}
