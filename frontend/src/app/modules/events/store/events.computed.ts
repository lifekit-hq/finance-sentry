import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';

import {DateUtils} from '../../../shared/utils/date.utils';
import {EVENT_SOURCE_LABELS, THIS_WEEK_DAYS} from '../constants/event/event.constants';
import {
  type CalendarWindow,
  type EventKind,
  type EventSourceStatusEntry,
  type EventWeekSection,
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
  fired: Signal<FiredEvent[]>;
  firedTotalCount: Signal<number>;
  firedPage: Signal<number>;
  firedPageSize: Signal<number>;
  firedStatus: Signal<AsyncStatus>;
}

export function eventsComputed(store: StateSignals) {
  return {
    /** The window the calendar view asks for: today through today + horizon, local dates. */
    calendarWindow: computed((): CalendarWindow => {
      const from = DateUtils.isoDateLocal(new Date());
      return {from, to: EventDayUtils.addDays(from, store.horizonDays())};
    }),
    /** Day groups split into "This week" and "Later"; a section with no days is left out. */
    upcomingSections: computed((): EventWeekSection[] => {
      const today = DateUtils.isoDateLocal(new Date());
      const groups = EventDayUtils.groupByDay(store.upcoming());
      const isThisWeek = (date: string) => EventDayUtils.daysUntil(date, today) < THIS_WEEK_DAYS;
      return (
        [
          {id: 'week', label: 'This week', groups: groups.filter(g => isThisWeek(g.date))},
          {id: 'later', label: 'Later', groups: groups.filter(g => !isThisWeek(g.date))},
        ] satisfies EventWeekSection[]
      ).filter(section => section.groups.length > 0);
    }),
    isUpcomingEmpty: computed(
      () => store.upcomingStatus() === 'success' && store.upcoming().length === 0
    ),
    /** Human labels of the sources that failed on the last read; empty when all answered. */
    unavailableSourceLabels: computed(() =>
      store
        .sources()
        .filter(s => s.status === 'unavailable')
        .map(s => EVENT_SOURCE_LABELS[s.source] ?? s.source)
    ),
    /** Chips light up only for an explicit pick; an empty selection means every kind. */
    isKindSelected: computed(() => {
      const selected = store.kinds();
      return (kind: EventKind): boolean => selected.includes(kind);
    }),
    isFiredEmpty: computed(() => store.firedStatus() === 'success' && store.fired().length === 0),
    hasFiredRows: computed(() => store.fired().length > 0),
    hasMoreFired: computed(
      () => store.firedPage() * store.firedPageSize() < store.firedTotalCount()
    ),
  };
}
