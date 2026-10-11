import {MS_PER_DAY} from '../../../shared/constants/date/date.constants';
import {DateUtils} from '../../../shared/utils/date.utils';
import {type EventDayGroup, type UpcomingEvent} from '../models/event/event.model';

export class EventDayUtils {
  /** An ISO date shifted by whole days. */
  public static addDays(isoDate: string, days: number): string {
    const base = new Date(`${isoDate}T00:00:00Z`);
    return DateUtils.isoDateUtc(new Date(base.getTime() + days * MS_PER_DAY));
  }

  /** Whole days from `today` to `isoDate`; negative for the past. */
  public static daysUntil(isoDate: string, today: string): number {
    const target = new Date(`${isoDate}T00:00:00Z`).getTime();
    const base = new Date(`${today}T00:00:00Z`).getTime();
    return Math.round((target - base) / MS_PER_DAY);
  }

  /** 'Today', 'Tomorrow', otherwise a short weekday + day + month, e.g. 'Mon 5 Oct'. */
  public static dayLabel(isoDate: string, today: string): string {
    const days = EventDayUtils.daysUntil(isoDate, today);
    if (days === 0) {
      return 'Today';
    }
    if (days === 1) {
      return 'Tomorrow';
    }
    return DateUtils.dayLabel(isoDate);
  }

  /** Groups events by calendar date, preserving the input order inside each day. */
  public static groupByDay(items: readonly UpcomingEvent[]): EventDayGroup[] {
    const groups = new Map<string, UpcomingEvent[]>();
    for (const item of items) {
      const bucket = groups.get(item.date);
      if (bucket) {
        bucket.push(item);
      } else {
        groups.set(item.date, [item]);
      }
    }
    return [...groups.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([date, dayItems]) => ({date, items: dayItems}));
  }
}
