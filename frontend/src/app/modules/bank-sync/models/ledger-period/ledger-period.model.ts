export type LedgerPeriod = 'this-month' | 'last-month' | '3m';

export interface LedgerPeriodDates {
  from: string;
  to: string;
}

export interface LedgerPeriodOption {
  id: LedgerPeriod;
  label: string;
}
