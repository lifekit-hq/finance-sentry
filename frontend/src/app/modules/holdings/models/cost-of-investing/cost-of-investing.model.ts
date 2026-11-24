import {type Nullable} from '@lifekit-hq/ui';

/**
 * `GET /wealth/<sleeve>/costs`: what the sleeve cost to run since 1 January, by kind. Amounts are in `currency`.
 * A view over rows already collected: nothing here is estimated beyond the FX notes.
 */
export interface SleeveCosts {
  asOf: string;
  yearStart: string;
  commissions: number;
  fees: number;
  /** Commissions on currency-pair trades and broker conversion surcharges; the spread is not recorded. */
  fxConversion: number;
  withholding: number;
  total: number;
  /** The costs as a fraction of returns before costs; null without a positive gross return. */
  shareOfReturns: Nullable<number>;
  /** Fees left out for want of a USD value. */
  unvaluedRows: number;
  /** Rows kept in the figures that were converted without a published rate. */
  approximateRows: number;
  fxReasons: string[];
  currency: string;
}
