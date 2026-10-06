import {HISTORY_RANGE_MONTHS} from '../constants/dashboard/dashboard.constants';
import {type HistoryRange} from '../models/dashboard/dashboard.model';

const MONTH_KEY_PAD = 2;
const ISO_DATE_LENGTH = 10;

export class DashboardRangeUtils {
  /**
   * Calendar months a range spans, the in-progress month included. Year-to-date reaches back
   * to January (August → 8).
   */
  public static windowMonths(range: HistoryRange, now = new Date()): number {
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
   * Inclusive `YYYY-MM-DD` bounds of the range's window, matching the months the dashboard
   * tiles total (first day of the window's first month through the last day of the current
   * month). `all` is unbounded, so it yields no bounds.
   */
  public static windowDates(range: HistoryRange, now = new Date()): {from?: string; to?: string} {
    if (range === 'all') {
      return {};
    }
    const monthEnd = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 0));
    return {
      from: `${DashboardRangeUtils.windowStartKey(range, now)}-01`,
      to: monthEnd.toISOString().slice(0, ISO_DATE_LENGTH),
    };
  }
}
