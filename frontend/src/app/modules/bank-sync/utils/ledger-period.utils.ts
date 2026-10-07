import {LEDGER_PERIODS} from '../constants/ledger-period/ledger-period.constants';
import {
  type LedgerPeriod,
  type LedgerPeriodDates,
} from '../models/ledger-period/ledger-period.model';
import {DashboardRangeUtils} from './dashboard-range.utils';

export class LedgerPeriodUtils {
  /**
   * Inclusive `YYYY-MM-DD` bounds of a ledger period: the dashboard range's window, so a
   * drill-down from a dashboard tile selects the same chip. `all` is unbounded.
   */
  public static dates(period: LedgerPeriod, now = new Date()): LedgerPeriodDates {
    const {from, to} = DashboardRangeUtils.windowDates(period, now);
    return {from: from ?? null, to: to ?? null};
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
