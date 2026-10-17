import {type LedgerPeriod} from '../../models/ledger-period/ledger-period.model';

export const LEDGER_PERIOD_EMPTY_PHRASES: Readonly<Record<LedgerPeriod, string>> = {
  'this-month': 'this month',
  'last-month': 'last month',
  '3m': 'the last 3 months',
};
