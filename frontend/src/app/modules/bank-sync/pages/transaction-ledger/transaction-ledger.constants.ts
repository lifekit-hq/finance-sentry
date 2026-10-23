import {type LedgerPeriod} from '../../models/ledger-period/ledger-period.model';

export const LEDGER_PERIOD_EMPTY_PHRASES: Readonly<Record<LedgerPeriod, string>> = {
  'this-month': 'this month',
  'last-month': 'last month',
  '3m': 'the last 3 months',
};

/** Closes "<amount> out …" in the summary line: names the period the figure covers. */
export const LEDGER_PERIOD_OUTFLOW_PHRASES: Readonly<Record<LedgerPeriod, string>> = {
  'this-month': 'this month',
  'last-month': 'last month',
  '3m': 'in the last 3 months',
};
export const LEDGER_ALL_TIME_OUTFLOW_PHRASE = 'all time';
export const LEDGER_CUSTOM_RANGE_OUTFLOW_PHRASE = 'in this range';
