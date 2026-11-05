import {type Nullable, type TagVariant} from '@lifekit-hq/ui';

import {type ProjectionRange} from '../projection/projection.model';

/** Mirrors the backend's `GoalsProgressStatus`, written by name. */
export type GoalsProgressStatus = 'Shown' | 'NoGoals';

/** Mirrors the backend's `GoalTrack`, written by name. */
export type GoalTrack =
  'OnTrack' | 'Behind' | 'NoTargetDate' | 'NoTargetAmount' | 'InsufficientHistory';

/**
 * One IPS goal against the current net worth (#454 S2, money-semantics §16). Amounts are in the response's
 * `baseCurrency`. `requiredMonthlySaving` is the level monthly saving that reaches the target by the date at the
 * low / central / high return; `projectedAtTargetDate` is where the current savings path lands by then.
 */
export interface GoalProgress {
  label: string;
  targetAmount: Nullable<number>;
  /** ISO date (yyyy-MM-dd). */
  targetDate: Nullable<string>;
  /** The owner's own wording. */
  priority: string;
  monthsLeft: Nullable<number>;
  requiredMonthlySaving: Nullable<ProjectionRange>;
  projectedAtTargetDate: Nullable<ProjectionRange>;
  alreadyReached: boolean;
  onTrack: GoalTrack;
  /** Why the verdict or a figure is withheld; one of `GOALS_PROGRESS_REASONS`. */
  reason: Nullable<string>;
}

/** The IPS contribution plan beside the measured savings path. */
export interface ContributionPlanComparison {
  plannedAmount: number;
  /** The IPS's own wording ("monthly", "weekly", ...). */
  cadence: string;
  plannedMonthly: Nullable<number>;
  actualMonthlySavings: Nullable<number>;
  /** Actual minus planned: positive while saving more than planned. */
  difference: Nullable<number>;
  reason: Nullable<string>;
}

/** Each IPS goal tracked against net worth plus savings. Rates are fractions (0.05 = 5%). */
export interface GoalsProgress {
  status: GoalsProgressStatus;
  currentNetWorth: Nullable<number>;
  monthlySavings: Nullable<number>;
  completeMonths: number;
  goals: GoalProgress[];
  contributionPlan: Nullable<ContributionPlanComparison>;
  realAnnualReturnRange: ProjectionRange;
  hasStaleSleeves: boolean;
  /** ISO code the amounts are expressed in: the profile base currency, USD when unset. */
  baseCurrency: string;
  basis: string;
  disclosure: string;
}

/** A goal as the card draws it: every line already written out. */
export interface GoalRowView {
  label: string;
  priority: string;
  trackLabel: string;
  trackVariant: TagVariant;
  /** "$130,000 by Sep 2028". */
  target: string;
  monthsLeft: Nullable<string>;
  required: Nullable<string>;
  projected: Nullable<string>;
  note: Nullable<string>;
}
