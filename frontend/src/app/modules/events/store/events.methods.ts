import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type EventKind,
  type EventsView,
  type FiredEventsPageResponse,
  type UpcomingEventsResult,
} from '../models/event/event.model';
import {type EventsState} from './events.state';

export function eventsMethods(store: WritableStateSource<EventsState>) {
  return {
    setView(view: EventsView): void {
      patchState(store, {view});
    },
    setHorizonDays(horizonDays: number): void {
      patchState(store, {horizonDays});
    },
    toggleKindLocal(kind: EventKind): void {
      patchState(store, state => ({
        kinds: state.kinds.includes(kind)
          ? state.kinds.filter(k => k !== kind)
          : [...state.kinds, kind],
      }));
    },
    setUpcomingLoading(): void {
      patchState(store, {upcomingStatus: 'loading', upcomingErrorCode: null});
    },
    setUpcoming(result: UpcomingEventsResult): void {
      patchState(store, {
        upcoming: result.items,
        sources: result.sources,
        upcomingStatus: 'idle',
        upcomingErrorCode: null,
      });
    },
    setUpcomingError(errorCode: Nullable<string>): void {
      patchState(store, {upcomingStatus: 'error', upcomingErrorCode: errorCode});
    },
    setFiredLoading(): void {
      patchState(store, {firedStatus: 'loading', firedErrorCode: null});
    },
    setFired(response: FiredEventsPageResponse): void {
      patchState(store, {
        fired: response.items,
        firedTotalCount: response.totalCount,
        firedPage: response.page,
        firedStatus: 'idle',
        firedErrorCode: null,
      });
    },
    appendFired(response: FiredEventsPageResponse): void {
      patchState(store, state => {
        const loaded = new Set(state.fired.map(event => event.alertId));
        return {
          fired: [...state.fired, ...response.items.filter(event => !loaded.has(event.alertId))],
          firedTotalCount: response.totalCount,
          firedPage: response.page,
          firedStatus: 'idle' as AsyncStatus,
          firedErrorCode: null,
        };
      });
    },
    setFiredError(errorCode: Nullable<string>): void {
      patchState(store, {firedStatus: 'error', firedErrorCode: errorCode});
    },
  };
}
