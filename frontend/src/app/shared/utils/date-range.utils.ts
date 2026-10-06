export interface DateRange {
  from: string | null;
  to: string;
}

export type RelativeRange = '1m' | '3m' | '6m' | 'ytd' | '1y' | 'all';

const ISO_DATE_LENGTH = 10;
const MONTHS_3 = 3;
const MONTHS_6 = 6;

export class DateRangeUtils {
  public static toIsoDate(d: Date): string {
    return d.toISOString().slice(0, ISO_DATE_LENGTH);
  }

  public static fromRelativeRange(range: RelativeRange, now = new Date()): DateRange {
    const to = DateRangeUtils.toIsoDate(now);
    const d = new Date(now);

    if (range === '1m') {
      d.setMonth(d.getMonth() - 1);
    } else if (range === 'ytd') {
      return {from: `${now.getUTCFullYear()}-01-01`, to};
    } else if (range === '3m') {
      d.setMonth(d.getMonth() - MONTHS_3);
    } else if (range === '6m') {
      d.setMonth(d.getMonth() - MONTHS_6);
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
