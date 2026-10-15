import {type Headline} from '../../../../shared/models/headline/headline.model';

/**
 * `GET /wealth/brokerage/growth`: the brokerage sleeve (IBKR + Inzhur, broker cash included) from the first
 * day its value history holds a value, split into what was paid in and what it earned. Amounts are in
 * `currency`; the split is null while a provider in `sourcesWithoutFlows` has never reported a deposit.
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
  /** Provider slugs that have never reported a deposit or withdrawal. */
  sourcesWithoutFlows: string[];
  headline: Headline;
  currency: string;
}

/** Which of the card's three states the response is in. */
export type BrokerageGrowthView = 'split' | 'no-flows' | 'short-history';
