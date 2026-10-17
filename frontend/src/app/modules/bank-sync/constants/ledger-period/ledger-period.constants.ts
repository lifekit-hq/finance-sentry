import {type LedgerPeriodOption} from '../../models/ledger-period/ledger-period.model';

export const LEDGER_PERIODS: readonly LedgerPeriodOption[] = [
  {id: 'this-month', label: 'This month'},
  {id: 'last-month', label: 'Last month'},
  {id: '3m', label: '3M'},
];

/** Chip-row value of "All time": no date filter. Quick period ids are words, so it never clashes. */
export const ALL_PERIODS_VALUE = 'all';
export const ALL_PERIODS_LABEL = 'All time';
