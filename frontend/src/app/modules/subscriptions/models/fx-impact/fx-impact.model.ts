import {type ChartPoint, type Nullable} from '@lifekit-hq/ui';

import {type SubscriptionKind} from '../subscription/subscription.model';

/** One point of a plan's rate history: units of the plan's currency per 1 base-currency unit. */
export interface FxRatePoint {
  date: string;
  unitsPerBase: number;
}

/**
 * Mirrors the backend's `FxImpactPlan`. Savings are in the base currency: positive when the plan got cheaper than
 * it was at the signing rate, negative when dearer. The `remaining*` fields are null while neither the term nor the
 * end date is known (an open-ended subscription).
 */
export interface FxImpactPlan {
  id: string;
  merchant: string;
  kind: SubscriptionKind;
  currency: string;
  paymentNative: number;
  signingDate: string;
  /** True when the signing date is only the first charge seen, not the plan's real start. */
  signingDateIsObserved: boolean;
  signingUnitsPerBase: number;
  currentUnitsPerBase: number;
  paymentCostAtSigning: number;
  paymentCostNow: number;
  costChangePercent: number;
  paymentsMade: number;
  paymentsRemaining: Nullable<number>;
  remainingCostAtSigningRate: Nullable<number>;
  remainingCostAtTodayRate: Nullable<number>;
  remainingSaving: Nullable<number>;
  paidSaving: number;
  rates: FxRatePoint[];
}

export interface FxImpactTotals {
  paidSaving: number;
  remainingSaving: number;
  totalSaving: number;
  /** True when a plan has no known remainder, so `remainingSaving` leaves it out. */
  hasUnknownRemainder: boolean;
}

/** Mirrors the backend's `FxImpactResponse`; an empty `plans` means the insight does not apply. */
export interface FxImpact {
  baseCurrency: string;
  plans: FxImpactPlan[];
  totals: FxImpactTotals;
}

/** A plan with the display values derived once, so the template only binds. */
export interface FxImpactPlanView {
  plan: FxImpactPlan;
  /** The rate history as chart points, oldest first. */
  chart: ChartPoint[];
  /** The payments already made came out cheaper (true) or dearer (false) than at the signing rate. */
  paidSaved: boolean;
  paidAmount: number;
  remainingSaved: boolean;
  remainingAmount: Nullable<number>;
}
