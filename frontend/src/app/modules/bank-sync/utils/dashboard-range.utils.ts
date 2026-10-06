import {DateRangeUtils, type DayWindowRange} from '../../../shared/utils/date-range.utils';
import {HISTORY_RANGE_MONTHS} from '../constants/dashboard/dashboard.constants';
import {type HistoryRange} from '../models/dashboard/dashboard.model';

const MONTH_KEY_PAD = 2;
const ISO_DATE_LENGTH = 10;
const MONTHS_PER_YEAR = 12;

export class DashboardRangeUtils {
  /**
   * Ranges measured in days (1W, MTD, 1M): their window starts on a UTC calendar day, which
   * the backend takes as `windowFrom`, instead of on a month boundary.
   */
  public static isDayWindow(range: HistoryRange): range is DayWindowRange {
    return range === '1w' || range === 'mtd' || range === '1m';
  }

  /** `YYYY-MM-DD` the day-resolution range starts on (UTC); `undefined` for month-based ranges. */
  public static windowFrom(range: HistoryRange, now = new Date()): string | undefined {
    return DashboardRangeUtils.isDayWindow(range)
      ? DateRangeUtils.toIsoDate(DateRangeUtils.dayWindowStart(range, now))
      : undefined;
  }

  /**
   * Calendar months a range spans, the in-progress month included. Year-to-date reaches back
   * to January (August → 8); a day-resolution range spans the month it starts in through the
   * current one (1 or 2).
   */
  public static windowMonths(range: HistoryRange, now = new Date()): number {
    if (DashboardRangeUtils.isDayWindow(range)) {
      const start = DateRangeUtils.dayWindowStart(range, now);
      return (
        (now.getUTCFullYear() - start.getUTCFullYear()) * MONTHS_PER_YEAR +
        (now.getUTCMonth() - start.getUTCMonth()) +
        1
      );
    }
    return range === 'ytd' ? now.getUTCMonth() + 1 : HISTORY_RANGE_MONTHS[range];
  }

  /**
   * Value for the backend's `months` parameter: how many COMPLETE months of history the charts
   * plot (the backend adds the in-progress one and clamps to at least 1). Year-to-date needs
   * the closed months since January.
   */
  public static months(range: HistoryRange, now = new Date()): number {
    return range === 'ytd' ? Math.max(now.getUTCMonth(), 1) : HISTORY_RANGE_MONTHS[range];
  }

  /** First `YYYY-MM` bucket inside the range's window (lexicographically comparable). */
  public static windowStartKey(range: HistoryRange, now = new Date()): string {
    const start = new Date(
      Date.UTC(
        now.getUTCFullYear(),
        now.getUTCMonth() - (DashboardRangeUtils.windowMonths(range, now) - 1),
        1
      )
    );
    return `${start.getUTCFullYear()}-${String(start.getUTCMonth() + 1).padStart(MONTH_KEY_PAD, '0')}`;
  }

  /**
   * Inclusive `YYYY-MM-DD` bounds of the range's window, matching what the dashboard tiles
   * total: for the month-based ranges, the first day of the window's first month through the
   * last day of the current month; for a day-resolution range, its start day through today
   * (UTC). `all` is unbounded, so it yields no bounds.
   */
  public static windowDates(range: HistoryRange, now = new Date()): {from?: string; to?: string} {
    if (range === 'all') {
      return {};
    }
    if (DashboardRangeUtils.isDayWindow(range)) {
      return {
        from: DashboardRangeUtils.windowFrom(range, now),
        to: DateRangeUtils.toIsoDate(now),
      };
    }
    const monthEnd = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 0));
    return {
      from: `${DashboardRangeUtils.windowStartKey(range, now)}-01`,
      to: monthEnd.toISOString().slice(0, ISO_DATE_LENGTH),
    };
  }

  /**
   * Query params that open the flow breakdown on the range's window: the same bounds the
   * other drill-downs carry, plus the `months` of history the dashboard loaded for it. `all`
   * has no lower bound, so only `to` is sent.
   */
  public static breakdownParams(
    range: HistoryRange,
    now = new Date()
  ): {from?: string; to: string; months: number} {
    const months = DashboardRangeUtils.months(range, now);
    if (range === 'all') {
      return {to: DateRangeUtils.toIsoDate(now), months};
    }
    // Month-based ranges run to month end; the breakdown stops at today, where the data does.
    return {
      from: DashboardRangeUtils.windowDates(range, now).from,
      to: DateRangeUtils.toIsoDate(now),
      months,
    };
  }
}
