import {formatDate, registerLocaleData} from '@angular/common';
import localeEnGb from '@angular/common/locales/en-GB';

import {APP_LOCALE, MS_PER_DAY} from '../constants/date/date.constants';

registerLocaleData(localeEnGb, APP_LOCALE);

const UTC = 'UTC';
const ISO_DATE_FORMAT = 'yyyy-MM-dd';
const MONTH_KEY_FORMAT = 'yyyy-MM';
const DAY_LABEL_FORMAT = 'EEE d MMM';
const DAY_LABEL_WITH_YEAR_FORMAT = 'EEE d MMM y';

/**
 * Date keys and labels on `formatDate`, always in {@link APP_LOCALE}. Every key comes in two
 * named zones: `Utc` reads the instant's UTC calendar day (what the dashboard windows and the
 * backend buckets use), `Local` reads the viewer's (what a "today" shown on screen means).
 */
export class DateUtils {
  /** `yyyy-MM-dd` of the instant's UTC calendar day. */
  public static isoDateUtc(date: Date): string {
    return formatDate(date, ISO_DATE_FORMAT, APP_LOCALE, UTC);
  }

  /** `yyyy-MM-dd` of the instant's calendar day in the viewer's time zone. */
  public static isoDateLocal(date: Date): string {
    return formatDate(date, ISO_DATE_FORMAT, APP_LOCALE);
  }

  /** `yyyy-MM` of the instant's UTC calendar month. */
  public static monthKeyUtc(date: Date): string {
    return formatDate(date, MONTH_KEY_FORMAT, APP_LOCALE, UTC);
  }

  /** `yyyy-MM` of the instant's calendar month in the viewer's time zone. */
  public static monthKeyLocal(date: Date): string {
    return formatDate(date, MONTH_KEY_FORMAT, APP_LOCALE);
  }

  /**
   * "Mon 5 Oct" for a `yyyy-MM-dd` calendar day (with the year, "Mon 5 Oct 2026", when asked).
   * A bare date is a calendar day, not an instant, so no zone shifts it.
   */
  public static dayLabel(isoDate: string, withYear = false): string {
    return formatDate(
      isoDate,
      withYear ? DAY_LABEL_WITH_YEAR_FORMAT : DAY_LABEL_FORMAT,
      APP_LOCALE
    );
  }

  /**
   * A past calendar day as a list heading: "Today", "Yesterday", otherwise "Mon 10 Aug" (the year
   * is added outside the current one). `now` is read in the viewer's time zone.
   */
  public static recentDayLabel(isoDate: string, now: Date = new Date()): string {
    if (isoDate === DateUtils.isoDateLocal(now)) {
      return 'Today';
    }
    if (isoDate === DateUtils.isoDateLocal(new Date(now.getTime() - MS_PER_DAY))) {
      return 'Yesterday';
    }
    const date = new Date(`${isoDate}T00:00:00`);
    if (Number.isNaN(date.getTime())) {
      return isoDate;
    }
    return DateUtils.dayLabel(isoDate, date.getFullYear() !== now.getFullYear());
  }
}
