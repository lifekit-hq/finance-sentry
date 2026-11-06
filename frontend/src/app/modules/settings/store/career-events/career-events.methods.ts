import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type CareerEvent} from '../../models/career-event/career-event.model';
import {type CareerEventsState} from './career-events.state';

export function careerEventsMethods(store: WritableStateSource<CareerEventsState>) {
  return {
    setEvents(events: CareerEvent[]): void {
      patchState(store, {events});
    },
    setBusy(id: string): void {
      patchState(store, {busyId: id, actionErrorCode: null});
    },
    setIdle(): void {
      patchState(store, {busyId: null, actionErrorCode: null});
    },
    setActionError(errorCode: Nullable<string>): void {
      patchState(store, {busyId: null, actionErrorCode: errorCode});
    },
    clearActionError(): void {
      patchState(store, {actionErrorCode: null});
    },
  };
}
