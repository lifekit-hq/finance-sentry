import {type LedgerPeriodOption} from '../../models/ledger-period/ledger-period.model';

/** The dashboard's range set (1W / MTD / 1M / 3M / YTD / 1Y / All), in its order. */
export const LEDGER_PERIODS: readonly LedgerPeriodOption[] = [
  {id: '1w', label: '1W'},
  {id: 'mtd', label: 'MTD'},
  {id: '1m', label: '1M'},
  {id: '3m', label: '3M'},
  {id: 'ytd', label: 'YTD'},
  {id: '1y', label: '1Y'},
  {id: 'all', label: 'All'},
];
