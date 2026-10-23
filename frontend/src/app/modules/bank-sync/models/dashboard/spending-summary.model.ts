import {type Nullable} from '@lifekit-hq/ui';

/** Outflow and top spending category of one account and date range, in the profile's base currency. */
export interface SpendingSummary {
  outflow: number;
  currency: string;
  /** Canonical category key; null when nothing was spent. */
  topCategory: Nullable<string>;
}
