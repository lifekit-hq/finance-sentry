import {inject, type Signal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../shared/utils/store-error.utils';
import {
  type CalendarWindow,
  type EventKind,
  type FiredEventsPageResponse,
  type UpcomingEventsResult,
} from '../models/event/event.model';
import {EventsService} from '../services/events.service';

interface EffectsStore {
  kinds: Signal<EventKind[]>;
  calendarWindow: Signal<CalendarWindow>;
  firedPage: Signal<number>;
  firedPageSize: Signal<number>;
  setHorizonDays: (days: number) => void;
  toggleKindLocal: (kind: EventKind) => void;
  setUpcomingLoading: () => void;
  setUpcoming: (result: UpcomingEventsResult) => void;
  setUpcomingError: (code: Nullable<string>) => void;
  setFiredLoading: () => void;
  setFired: (response: FiredEventsPageResponse) => void;
  appendFired: (response: FiredEventsPageResponse) => void;
  setFiredError: (code: Nullable<string>) => void;
}

export function eventsEffects(store: EffectsStore) {
  const api = inject(EventsService);

  const fetchUpcoming = () => {
    const {from, to} = store.calendarWindow();
    return api.getUpcoming(from, to, store.kinds()).pipe(
      tap(result => store.setUpcoming(result)),
      StoreErrorUtils.catchAndSetError({
        setError: (code: Nullable<string>) => store.setUpcomingError(code),
      })
    );
  };

  const loadUpcoming = rxMethod<void>(
    pipe(
      tap(() => store.setUpcomingLoading()),
      switchMap(fetchUpcoming)
    )
  );

  return {
    loadUpcoming,
    selectHorizon(days: number): void {
      store.setHorizonDays(days);
      loadUpcoming();
    },
    toggleKind(kind: EventKind): void {
      store.toggleKindLocal(kind);
      loadUpcoming();
    },
    loadFired: rxMethod<void>(
      pipe(
        tap(() => store.setFiredLoading()),
        switchMap(() =>
          api.getFired(1, store.firedPageSize()).pipe(
            tap(response => store.setFired(response)),
            StoreErrorUtils.catchAndSetError({
              setError: (code: Nullable<string>) => store.setFiredError(code),
            })
          )
        )
      )
    ),
    loadMoreFired: rxMethod<void>(
      pipe(
        tap(() => store.setFiredLoading()),
        switchMap(() =>
          api.getFired(store.firedPage() + 1, store.firedPageSize()).pipe(
            tap(response => store.appendFired(response)),
            StoreErrorUtils.catchAndSetError({
              setError: (code: Nullable<string>) => store.setFiredError(code),
            })
          )
        )
      )
    ),
  };
}

interface HookStore {
  loadUpcoming: () => void;
  loadFired: () => void;
}

export function eventsHooks(store: HookStore): void {
  store.loadUpcoming();
  store.loadFired();
}
