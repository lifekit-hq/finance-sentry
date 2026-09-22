export type EventKind = 'earnings' | 'ex_dividend' | 'filing_due' | 'macro' | 'thesis_catalyst';
export type EventSourceName = 'corporate' | 'macro' | 'theses' | 'filings';
export type EventSourceStatus = 'ok' | 'unavailable';
export type FiredEventKind =
  'EarningsAhead' | 'FilingLanded' | 'NewsCluster' | 'MarketStructure' | 'BudgetBreach';
export type EventOutcome =
  'verdict' | 'judged_immaterial' | 'silent' | 'awaiting' | 'not_delivered';
export type EventSeverity = 'Error' | 'Warning' | 'Info';
export type EventsView = 'calendar' | 'fired';
export type EventTagVariant = 'success' | 'warning' | 'error' | 'info' | 'neutral';

export interface UpcomingEvent {
  kind: EventKind;
  /** ISO calendar date, e.g. '2026-10-01'. */
  date: string;
  /** ISO time of day, e.g. '14:00:00', when the source knows it. */
  time: Nullable<string>;
  subject: string;
  title: string;
  detail: Nullable<string>;
  isEstimate: boolean;
  source: string;
  referenceId: Nullable<string>;
}

export interface EventSourceStatusEntry {
  source: EventSourceName;
  status: EventSourceStatus;
}

export interface UpcomingEventsResult {
  items: UpcomingEvent[];
  from: string;
  to: string;
  sources: EventSourceStatusEntry[];
}

export interface EventDelivery {
  eventId: string;
  disposition: string;
  dispatchedAt: Nullable<string>;
  deliveredAt: Nullable<string>;
}

export interface EventVerdict {
  text: string;
  notified: boolean;
  recordedAt: string;
}

export interface FiredEvent {
  alertId: string;
  kind: FiredEventKind;
  severity: EventSeverity;
  subject: string;
  title: string;
  message: string;
  occurredAt: string;
  isRead: boolean;
  delivery: Nullable<EventDelivery>;
  verdict: Nullable<EventVerdict>;
  outcome: EventOutcome;
}

export interface FiredEventsPageResponse {
  items: FiredEvent[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface EventDayGroup {
  /** ISO calendar date the group holds. */
  date: string;
  items: UpcomingEvent[];
}

export interface CalendarWindow {
  from: string;
  to: string;
}
