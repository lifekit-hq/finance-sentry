import {DEFAULT_EVENT_HORIZON_DAYS, FIRED_PAGE_SIZE} from '../constants/event/event.constants';
import {
  type EventKind,
  type EventSourceStatusEntry,
  type EventsView,
  type FiredEvent,
  type UpcomingEvent,
} from '../models/event/event.model';

export interface EventsState {
  view: EventsView;
  horizonDays: number;
  /** Selected kinds; empty means every kind. */
  kinds: EventKind[];
  upcoming: UpcomingEvent[];
  sources: EventSourceStatusEntry[];
  upcomingStatus: AsyncStatus;
  upcomingErrorCode: Nullable<string>;
  fired: FiredEvent[];
  firedTotalCount: number;
  firedPage: number;
  firedPageSize: number;
  firedStatus: AsyncStatus;
  firedErrorCode: Nullable<string>;
}

export const initialEventsState: EventsState = {
  view: 'calendar',
  horizonDays: DEFAULT_EVENT_HORIZON_DAYS,
  kinds: [],
  upcoming: [],
  sources: [],
  upcomingStatus: 'idle',
  upcomingErrorCode: null,
  fired: [],
  firedTotalCount: 0,
  firedPage: 1,
  firedPageSize: FIRED_PAGE_SIZE,
  firedStatus: 'idle',
  firedErrorCode: null,
};
