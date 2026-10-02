const MONTH_KEY_PAD = 2;
const MONTHS_PER_YEAR = 12;

/**
 * Helpers for `yyyy-MM` month keys — the bucket key the dashboard statistics use.
 * Pure string/date math; no time-zone surprises because days never enter the picture.
 */
export class MonthKeyUtils {
  /** The current month's key in UTC, e.g. "2026-09". */
  public static currentUtc(): string {
    const now = new Date();
    return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(MONTH_KEY_PAD, '0')}`;
  }

  /** Shifts a month key by `delta` months (negative = into the past). */
  public static shift(month: string, delta: number): string {
    const [year, monthNumber] = month.split('-').map(Number);
    const zeroBased = (year ?? 0) * MONTHS_PER_YEAR + ((monthNumber ?? 1) - 1) + delta;
    const shiftedYear = Math.floor(zeroBased / MONTHS_PER_YEAR);
    const shiftedMonth = (zeroBased % MONTHS_PER_YEAR) + 1;
    return `${shiftedYear}-${String(shiftedMonth).padStart(MONTH_KEY_PAD, '0')}`;
  }

  /** The first day of a month key as a local date, e.g. "2026-09" → 1 Sep 2026. */
  public static toDate(month: string): Date {
    const [year, monthNumber] = month.split('-').map(Number);
    return new Date(year ?? 0, (monthNumber ?? 1) - 1, 1);
  }

  /** The month key of a local date — the inverse of `toDate`. */
  public static fromDate(date: Date): string {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(MONTH_KEY_PAD, '0')}`;
  }
}
