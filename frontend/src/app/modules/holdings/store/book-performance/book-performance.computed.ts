import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {PercentUtils} from '../../../../shared/utils/percent.utils';
import {
  BOOK_TWR_CURRENCY,
  BOOK_TWR_METHOD,
  BOOK_TWR_SCOPE,
  PERCENT_POINTS,
  RETURN_PERCENT_DIGITS,
} from '../../constants/book-performance/book-performance.constants';
import {
  type BookPerformance,
  type BookVsSpy,
} from '../../models/book-performance/book-performance.model';

interface StateSignals {
  performance: Signal<Nullable<BookPerformance>>;
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
  };
}
