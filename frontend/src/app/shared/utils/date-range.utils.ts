export interface DateRange {
  from: string | null;
  to: string;
}

/** Windows measured in days: they start on a UTC calendar day rather than on a month boundary. */
export type DayWindowRange = '1w' | 'mtd' | '1m';

export type RelativeRange = DayWindowRange | '3m' | 'ytd' | '1y' | 'all';

const ISO_DATE_LENGTH = 10;
const MONTHS_3 = 3;
const DAYS_IN_WEEK = 7;

export class DateRangeUtils {
  public static toIsoDate(d: Date): string {
    return d.toISOString().slice(0, ISO_DATE_LENGTH);
  }

  /**
   * First UTC calendar day of a day-resolution window, running through `now`'s day. Like the
   * rest of the dashboard it is anchored in UTC, not the viewer's zone.
   * - `1w`: the last seven days, today included.
   * - `mtd`: the first of the current month.
   * - `1m`: the same day one month ago, held to that month's last day (Mar 31 → Feb 28).
   */
  public static dayWindowStart(range: DayWindowRange, now = new Date()): Date {
    const year = now.getUTCFullYear();
    const month = now.getUTCMonth();
    const day = now.getUTCDate();

    if (range === '1w') {
      return new Date(Date.UTC(year, month, day - (DAYS_IN_WEEK - 1)));
    }
    if (range === 'mtd') {
      return new Date(Date.UTC(year, month, 1));
    }
    const lastDayOfPreviousMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
    return new Date(Date.UTC(year, month - 1, Math.min(day, lastDayOfPreviousMonth)));
  }

  public static fromRelativeRange(range: RelativeRange, now = new Date()): DateRange {
    const to = DateRangeUtils.toIsoDate(now);

    if (range === '1w' || range === 'mtd' || range === '1m') {
      return {from: DateRangeUtils.toIsoDate(DateRangeUtils.dayWindowStart(range, now)), to};
    }
    if (range === 'ytd') {
      return {from: `${now.getUTCFullYear()}-01-01`, to};
    }

    const d = new Date(now);
    if (range === '3m') {
      d.setMonth(d.getMonth() - MONTHS_3);
    } else if (range === '1y') {
      d.setFullYear(d.getFullYear() - 1);
    } else {
      return {from: null, to};
    }

    return {from: DateRangeUtils.toIsoDate(d), to};
  }

  public static toHttpParams(range: DateRange): Record<string, string> {
    return range.from ? {from: range.from, to: range.to} : {to: range.to};
  }
}
