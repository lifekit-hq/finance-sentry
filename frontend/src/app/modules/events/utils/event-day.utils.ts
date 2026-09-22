import {type EventDayGroup, type UpcomingEvent} from '../models/event/event.model';

const ISO_DATE_LENGTH = 10;
const HOURS_PER_DAY = 24;
const MINUTES_PER_HOUR = 60;
const SECONDS_PER_MINUTE = 60;
const MS_PER_SECOND = 1000;
const MS_PER_MINUTE = SECONDS_PER_MINUTE * MS_PER_SECOND;
const MS_PER_DAY = HOURS_PER_DAY * MINUTES_PER_HOUR * MS_PER_MINUTE;
const DAY_LABEL_LOCALE = 'en-GB';

export class EventDayUtils {
  /** The local calendar date of a JS Date as 'YYYY-MM-DD'. */
  public static toIsoDate(date: Date): string {
    const offsetMs = date.getTimezoneOffset() * MS_PER_MINUTE;
    return new Date(date.getTime() - offsetMs).toISOString().slice(0, ISO_DATE_LENGTH);
  }

  /** An ISO date shifted by whole days. */
  public static addDays(isoDate: string, days: number): string {
    const base = new Date(`${isoDate}T00:00:00Z`);
    return new Date(base.getTime() + days * MS_PER_DAY).toISOString().slice(0, ISO_DATE_LENGTH);
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
    return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString(DAY_LABEL_LOCALE, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      timeZone: 'UTC',
    });
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
