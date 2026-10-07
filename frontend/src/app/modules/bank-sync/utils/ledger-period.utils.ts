import {DateRangeUtils} from '../../../shared/utils/date-range.utils';
import {DashboardRangeUtils} from './dashboard-range.utils';

export type LedgerPeriod = 'this-month' | 'last-month' | '3m';

export interface LedgerPeriodDates {
  from: string;
  to: string;
}

export interface LedgerPeriodOption {
  id: LedgerPeriod;
  label: string;
}

export const LEDGER_PERIODS: readonly LedgerPeriodOption[] = [
  {id: 'this-month', label: 'This month'},
  {id: 'last-month', label: 'Last month'},
  {id: '3m', label: '3M'},
];

export class LedgerPeriodUtils {
  /**
   * Inclusive `YYYY-MM-DD` bounds of a ledger period, UTC-anchored like the dashboard presets.
   * This month and 3M are the dashboard's MTD and 3M windows; last month is the previous
   * calendar month, first through last day.
   */
  public static dates(period: LedgerPeriod, now = new Date()): LedgerPeriodDates {
    if (period === 'last-month') {
      const first = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1));
      const last = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 0));
      return {from: DateRangeUtils.toIsoDate(first), to: DateRangeUtils.toIsoDate(last)};
    }
    const {from, to} = DashboardRangeUtils.windowDates(period === '3m' ? '3m' : 'mtd', now);
    return {from: from as string, to: to as string};
  }

  /** The period whose bounds equal the given date filter, or null for any other range. */
  public static match(
    from: Nullable<string>,
    to: Nullable<string>,
    now = new Date()
  ): Nullable<LedgerPeriod> {
    return (
      LEDGER_PERIODS.find(p => {
        const d = LedgerPeriodUtils.dates(p.id, now);
        return d.from === from && d.to === to;
      })?.id ?? null
    );
  }
}
