import {
  type EventKind,
  type EventOutcome,
  type EventSourceName,
  type EventsView,
  type EventTagVariant,
  type FiredEventKind,
} from '../../models/event/event.model';

export interface EventKindMeta {
  label: string;
  variant: EventTagVariant;
}

export interface OutcomeMeta {
  label: string;
  variant: EventTagVariant;
  description: string;
}

export interface EventsViewOption {
  id: EventsView;
  label: string;
}

const HORIZON_WEEK = 7;
const HORIZON_MONTH = 30;
const HORIZON_QUARTER = 90;

export const EVENT_HORIZON_DAYS: readonly number[] = [HORIZON_WEEK, HORIZON_MONTH, HORIZON_QUARTER];
export const DEFAULT_EVENT_HORIZON_DAYS = HORIZON_MONTH;
export const FIRED_PAGE_SIZE = 20;

export const EVENTS_VIEW_OPTIONS: readonly EventsViewOption[] = [
  {id: 'calendar', label: 'Calendar'},
  {id: 'fired', label: 'Fired'},
];

export const EVENT_KIND_ORDER: readonly EventKind[] = [
  'earnings',
  'ex_dividend',
  'filing_due',
  'macro',
  'thesis_catalyst',
];

export const EVENT_KIND_META_REGISTRY = {
  ['earnings']: {label: 'Earnings', variant: 'info'},
  ['ex_dividend']: {label: 'Ex-dividend', variant: 'success'},
  ['filing_due']: {label: 'Filing due', variant: 'neutral'},
  ['macro']: {label: 'Macro', variant: 'warning'},
  ['thesis_catalyst']: {label: 'Thesis catalyst', variant: 'info'},
} satisfies Record<EventKind, EventKindMeta>;

export const FIRED_KIND_META_REGISTRY = {
  ['EarningsAhead']: {label: 'Earnings ahead', variant: 'info'},
  ['FilingLanded']: {label: 'Filing landed', variant: 'neutral'},
  ['NewsCluster']: {label: 'News cluster', variant: 'warning'},
  ['MarketStructure']: {label: 'Market move', variant: 'warning'},
  ['BudgetBreach']: {label: 'Budget breach', variant: 'error'},
} satisfies Record<FiredEventKind, EventKindMeta>;

export const OUTCOME_META_REGISTRY = {
  ['verdict']: {
    label: 'Verdict',
    variant: 'success',
    description: 'The reader judged it and told you.',
  },
  ['judged_immaterial']: {
    label: 'Judged immaterial',
    variant: 'neutral',
    description: 'The reader judged it and stayed quiet on purpose.',
  },
  ['silent']: {
    label: 'Silent',
    variant: 'neutral',
    description: 'Acknowledged, nothing recorded - silence is the answer.',
  },
  ['awaiting']: {
    label: 'Awaiting',
    variant: 'warning',
    description: 'Not read yet.',
  },
  ['not_delivered']: {
    label: 'Not delivered',
    variant: 'error',
    description: 'Suppressed or failed before it reached the reader.',
  },
} satisfies Record<EventOutcome, OutcomeMeta>;

export const EVENT_SOURCE_LABELS = {
  ['corporate']: 'earnings and ex-dividend dates',
  ['macro']: 'the macro calendar',
  ['theses']: 'thesis catalysts',
  ['filings']: 'filing due dates',
} satisfies Record<EventSourceName, string>;
