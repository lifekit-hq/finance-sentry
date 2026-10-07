import {type LedgerPeriodOption} from '../../models/ledger-period/ledger-period.model';

export const LEDGER_PERIODS: readonly LedgerPeriodOption[] = [
  {id: 'this-month', label: 'This month'},
  {id: 'last-month', label: 'Last month'},
  {id: '3m', label: '3M'},
];
