import {type Nullable} from '@lifekit-hq/ui';

import {type Headline} from '../../../../shared/models/headline/headline.model';

/** The sleeves with a contributions-vs-growth split: `GET /wealth/<sleeve>/growth`. */
export type GrowthSleeve = 'brokerage' | 'crypto';

/**
 * `GET /wealth/<sleeve>/growth`: the brokerage sleeve (IBKR + Inzhur, broker cash included) or the crypto
 * venues from the first day its value history holds a value, split into what was paid in and what it earned.
 * Amounts are in `currency`; the split is null while a provider in `sourcesWithoutFlows` has never reported a
 * deposit.
 */
export interface BrokerageGrowth {
  /** ISO date the daily value history starts; null with no brokerage value yet. */
  seriesStart: Nullable<string>;
  from: Nullable<string>;
  to: Nullable<string>;
  startValue: Nullable<number>;
  endValue: Nullable<number>;
  /** Deposits less withdrawals the brokers report. */
  contributed: Nullable<number>;
  /** Value that arrived with a newly connected provider: neither paid in nor earned. */
  newlyLinked: Nullable<number>;
  growth: Nullable<number>;
  income: Nullable<number>;
  fees: Nullable<number>;
  taxes: Nullable<number>;
  /** Modified Dietz over the window, as a fraction; the headline carries it in percentage points. */
  moneyWeightedReturn: Nullable<number>;
  /**
   * M3 over the same closes and flows, as a fraction; null while the split is withheld, when value moved with
   * no recorded flow, or from an older API build.
   */
  timeWeightedReturn?: Nullable<number>;
  /** Provider slugs that have never reported a deposit or withdrawal. */
  sourcesWithoutFlows: string[];
  headline: Headline;
  currency: string;
}

/** Which of the card's three states the response is in. */
export type BrokerageGrowthView = 'split' | 'no-flows' | 'short-history';

/** The words that differ between the sleeves' cards. */
export interface GrowthSleeveCopy {
  title: string;
  /** What the figures cover, read before "since <date>". */
  scope: string;
  /** What the growth line already includes. */
  includes: string;
  /** The quality badge's accessible label. */
  badgeLabel: string;
}

/** The timing gap (presentation pattern P8): the investments' return against the money's, signed. */
export interface GrowthTimingGap {
  investments: string;
  money: string;
  /** The distance between the two, in percentage points, unsigned. */
  points: string;
}
