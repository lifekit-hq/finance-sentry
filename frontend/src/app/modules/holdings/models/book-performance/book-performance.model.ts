import {type Nullable} from '@lifekit-hq/ui';

import {
  type Headline,
  type HeadlineQuality,
  type HeadlineReason,
} from '../../../../shared/models/headline/headline.model';

/**
 * One lookback window of `GET /radar/book-performance` (the fields the investments tab reads): the brokerage
 * book's time-weighted return (M3) against SPY's adjusted-close return over the same dates (M9). Returns are
 * USD fractions; dates are ISO.
 */
export interface BookPerformancePeriod {
  bookTwr: Nullable<number>;
  spyTwr: Nullable<number>;
  /** Book minus SPY; null unless SPY was measured over the book's own window. */
  delta: Nullable<number>;
  bookTwrFrom?: Nullable<string>;
  bookTwrTo?: Nullable<string>;
  bookTwrReasons?: Nullable<HeadlineReason[]>;
  bookTwrQuality?: Nullable<HeadlineQuality>;
}

export interface BookPerformance {
  /** In the order 1W, 1M, 3M, 1Y; a window without enough history is left out. */
  periods: BookPerformancePeriod[];
  asOf: string;
}

/** The book against SPY since the first day of the longest window both were measured over (P2). */
export interface BookVsSpy {
  from: string;
  bookReturn: string;
  benchmarkReturn: string;
  /** The book TWR's quality, for its badge. */
  headline: Nullable<Headline>;
}
