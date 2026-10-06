import {HISTORY_RANGE_MONTHS} from '../constants/dashboard/dashboard.constants';
import {type HistoryRange} from '../models/dashboard/dashboard.model';

const MONTH_KEY_PAD = 2;

export class DashboardRangeUtils {
  /**
   * Calendar months a range spans, the in-progress month included. Year-to-date reaches back
   * to January (August → 8).
   */
  public static windowMonths(range: HistoryRange, now = new Date()): number {
    return range === 'ytd' ? now.getUTCMonth() + 1 : HISTORY_RANGE_MONTHS[range];
  }

  /**
   * Value for the backend's `months` parameter, which counts COMPLETE months before the
   * in-progress one and clamps to at least 1. A one-month window (or January year-to-date)
   * therefore still returns the previous month; `windowStartKey` cuts it off client-side.
   */
  public static months(range: HistoryRange, now = new Date()): number {
    return Math.max(DashboardRangeUtils.windowMonths(range, now) - 1, 1);
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
}
