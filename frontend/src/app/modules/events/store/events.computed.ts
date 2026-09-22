import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {EVENT_SOURCE_LABELS} from '../constants/event/event.constants';
import {
  type CalendarWindow,
  type EventDayGroup,
  type EventKind,
  type EventSourceStatusEntry,
  type FiredEvent,
  type UpcomingEvent,
} from '../models/event/event.model';
import {EventDayUtils} from '../utils/event-day.utils';

interface StateSignals {
  horizonDays: Signal<number>;
  kinds: Signal<EventKind[]>;
  upcoming: Signal<UpcomingEvent[]>;
  sources: Signal<EventSourceStatusEntry[]>;
  upcomingStatus: Signal<AsyncStatus>;
  upcomingErrorCode: Signal<Nullable<string>>;
  fired: Signal<FiredEvent[]>;
  firedTotalCount: Signal<number>;
  firedStatus: Signal<AsyncStatus>;
  firedErrorCode: Signal<Nullable<string>>;
}

const DEFAULT_UPCOMING_ERROR = 'Failed to load upcoming events.';
const DEFAULT_FIRED_ERROR = 'Failed to load fired events.';

export function eventsComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    /** The window the calendar view asks for: today through today + horizon, local dates. */
    calendarWindow: computed((): CalendarWindow => {
      const from = EventDayUtils.toIsoDate(new Date());
      return {from, to: EventDayUtils.addDays(from, store.horizonDays())};
    }),
    isUpcomingLoading: computed(() => store.upcomingStatus() === 'loading'),
    upcomingErrorMessage: computed(() => {
      if (store.upcomingStatus() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.upcomingErrorCode()) ?? DEFAULT_UPCOMING_ERROR;
    }),
    upcomingByDay: computed((): EventDayGroup[] => EventDayUtils.groupByDay(store.upcoming())),
    isUpcomingEmpty: computed(
      () => store.upcomingStatus() === 'idle' && store.upcoming().length === 0
    ),
    /** Human labels of the sources that failed on the last read; empty when all answered. */
    unavailableSourceLabels: computed(() =>
      store
        .sources()
        .filter(s => s.status === 'unavailable')
        .map(s => EVENT_SOURCE_LABELS[s.source] ?? s.source)
    ),
    isKindSelected: computed(() => {
      const selected = store.kinds();
      return (kind: EventKind): boolean => selected.length === 0 || selected.includes(kind);
    }),
    isFiredLoading: computed(() => store.firedStatus() === 'loading'),
    firedErrorMessage: computed(() => {
      if (store.firedStatus() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.firedErrorCode()) ?? DEFAULT_FIRED_ERROR;
    }),
    isFiredEmpty: computed(() => store.firedStatus() === 'idle' && store.fired().length === 0),
    hasMoreFired: computed(() => store.fired().length < store.firedTotalCount()),
  };
}
