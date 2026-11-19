import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {PercentUtils} from '../../../../shared/utils/percent.utils';
import {
  BOOK_PERFORMANCE_THREE_MONTHS,
  BOOK_TWR_CURRENCY,
  BOOK_TWR_METHOD,
  BOOK_TWR_SCOPE,
  PERCENT_POINTS,
  RETURN_PERCENT_DIGITS,
  SUSTAINED_FLAG_SCOPES,
} from '../../constants/book-performance/book-performance.constants';
import {
  type BookPerformance,
  type BookVsSpy,
  type BookVsSpyShort,
  type SustainedUnderperformance,
} from '../../models/book-performance/book-performance.model';
import {type TrackRecord} from '../../models/book-performance/track-record.model';

interface StateSignals {
  performance: Signal<Nullable<BookPerformance>>;
  trackRecord: Signal<Nullable<TrackRecord>>;
}

export function bookPerformanceComputed(store: StateSignals) {
  return {
    /**
     * P2 over the longest window where the book and SPY were measured over the same dates; null when none
     * was, so the card stays out of the way instead of comparing different windows.
     */
    comparison: computed<Nullable<BookVsSpy>>(() => {
      const period = [...(store.performance()?.periods ?? [])]
        .reverse()
        .find(p => p.bookTwr != null && p.spyTwr != null && p.delta != null && p.bookTwrFrom);
      if (
        !period?.bookTwrFrom ||
        !period.bookTwrTo ||
        period.bookTwr == null ||
        period.spyTwr == null
      ) {
        return null;
      }
      const window = {from: period.bookTwrFrom, to: period.bookTwrTo};
      return {
        from: period.bookTwrFrom,
        bookReturn: PercentUtils.signed(period.bookTwr * PERCENT_POINTS, RETURN_PERCENT_DIGITS),
        benchmarkReturn: PercentUtils.signed(period.spyTwr * PERCENT_POINTS, RETURN_PERCENT_DIGITS),
        qqqReturn:
          period.qqqTwr == null
            ? null
            : PercentUtils.signed(period.qqqTwr * PERCENT_POINTS, RETURN_PERCENT_DIGITS),
        headline: period.bookTwrQuality
          ? {
              amount: null,
              percent: period.bookTwr * PERCENT_POINTS,
              method: BOOK_TWR_METHOD,
              scope: BOOK_TWR_SCOPE,
              window,
              quality: period.bookTwrQuality,
              reasons: period.bookTwrReasons ?? [],
              currency: BOOK_TWR_CURRENCY,
            }
          : null,
      };
    }),
    /** The 3M period's pair of returns, shown beside the longer window; null when 3M was not measured for both. */
    shortComparison: computed<Nullable<BookVsSpyShort>>(() => {
      const period = store
        .performance()
        ?.periods.find(p => p.period === BOOK_PERFORMANCE_THREE_MONTHS);
      if (
        period?.bookTwr == null ||
        period.spyTwr == null ||
        period.delta == null ||
        !period.bookTwrFrom
      ) {
        return null;
      }
      return {
        bookReturn: PercentUtils.signed(period.bookTwr * PERCENT_POINTS, RETURN_PERCENT_DIGITS),
        benchmarkReturn: PercentUtils.signed(period.spyTwr * PERCENT_POINTS, RETURN_PERCENT_DIGITS),
      };
    }),
    /**
     * The first book or sleeve row of the rule window the weekly job flagged (money-semantics §10); the
     * flag is read as stored, never recomputed. Null when none is flagged or the record was not read.
     */
    sustained: computed<Nullable<SustainedUnderperformance>>(() => {
      const record = store.trackRecord();
      const row = record?.rows.find(
        r =>
          r.sustainedUnderperformance &&
          SUSTAINED_FLAG_SCOPES.includes(r.scope) &&
          r.window === record.rule.window
      );
      return row ? {label: row.label} : null;
    }),
  };
}
