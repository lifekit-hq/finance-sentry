import {type Nullable} from '@lifekit-hq/ui';
/**
 * A projected figure as a range (finance-framework C8): `central` at the central return assumption, `low` and
 * `high` the smallest and largest of the three scenarios, so `low <= central <= high` always holds.
 */
export interface ProjectionRange {
  low: number;
  central: number;
  high: number;
}

/**
 * Whether the person's career events shaped a scenario (#454 S5), written by name. `None` never reaches the client (the
 * block is absent); `Unreadable` means events count but this host cannot read them, so no scenario figure exists.
 */
export type ScenarioStatus = 'None' | 'Applied' | 'Unreadable';

/**
 * The savings path with the person's career events applied, beside the baseline and never replacing it. `label` and
 * `basis` are the words it must be shown with ("a scenario you entered, not a forecast").
 */
export interface NetWorthScenario {
  status: ScenarioStatus;
  eventCount: number;
  /** The scenario's range on the baseline's horizon; null unless `status` is `Applied`. */
  projectedNetWorth: Nullable<ProjectionRange>;
  label: string;
  basis: string;
}

/** Mirrors the backend's `NetWorthProjectionStatus`, written by name. */
export type NetWorthProjectionStatus = 'Projected' | 'InsufficientHistory';

/**
 * The twelve-month net-worth projection (M10): the latest net worth plus the median monthly saving,
 * compounded at the shared real-return assumption, as a range in today's money. Rates are fractions
 * (0.05 = 5%); amounts are in `baseCurrency`.
 */
export interface NetWorthProjection {
  status: NetWorthProjectionStatus;
  horizonMonths: number;
  currentNetWorth: number;
  monthlySavings: number;
  /** How many complete months the median saving is taken over. */
  completeMonths: number;
  /** Present only while the status is `Projected`. */
  projectedNetWorth: Nullable<ProjectionRange>;
  realAnnualReturnRange: ProjectionRange;
  hasStaleSleeves: boolean;
  /** ISO code the amounts are expressed in: the profile base currency, USD when unset. */
  baseCurrency: string;
  /** Present only when the person has career events that count. */
  scenario?: Nullable<NetWorthScenario>;
}
