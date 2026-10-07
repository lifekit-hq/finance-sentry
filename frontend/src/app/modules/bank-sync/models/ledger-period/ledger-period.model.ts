import {type HistoryRange} from '../dashboard/dashboard.model';

/** The ledger's quick periods are the dashboard's history ranges, so a drill-down lands on its chip. */
export type LedgerPeriod = HistoryRange;

/** Inclusive `YYYY-MM-DD` bounds; either is null for an open end (`all` has neither). */
export interface LedgerPeriodDates {
  from: Nullable<string>;
  to: Nullable<string>;
}

export interface LedgerPeriodOption {
  id: LedgerPeriod;
  label: string;
}
