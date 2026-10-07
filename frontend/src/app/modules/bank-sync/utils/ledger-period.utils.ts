import {DateRangeUtils} from '../../../shared/utils/date-range.utils';
import {LEDGER_PERIODS} from '../constants/ledger-period/ledger-period.constants';
import {
  type LedgerPeriod,
  type LedgerPeriodDates,
} from '../models/ledger-period/ledger-period.model';
import {DashboardRangeUtils} from './dashboard-range.utils';

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
