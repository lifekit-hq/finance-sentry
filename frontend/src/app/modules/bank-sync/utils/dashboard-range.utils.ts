import {HISTORY_RANGE_MONTHS} from '../constants/dashboard/dashboard.constants';
import {type HistoryRange} from '../models/dashboard/dashboard.model';

export class DashboardRangeUtils {
  /**
   * Calendar months the backend should aggregate for a range. Year-to-date reaches back to
   * January, so it counts the in-progress month too (August → 8).
   */
  public static months(range: HistoryRange, now = new Date()): number {
    return range === 'ytd' ? now.getUTCMonth() + 1 : HISTORY_RANGE_MONTHS[range];
  }
}
