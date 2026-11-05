import {type Nullable} from '@lifekit-hq/ui';

import {APP_LOCALE} from '../../../shared/constants/date/date.constants';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {PercentUtils} from '../../../shared/utils/percent.utils';
import {
  GOAL_TRACK_LABEL,
  GOAL_TRACK_VARIANT,
  GOALS_PROGRESS_BASIS,
  GOALS_PROGRESS_REASONS,
} from '../constants/goals-progress/goals-progress.constants';
import {
  type GoalProgress,
  type GoalRowView,
  type GoalsProgress,
} from '../models/goals-progress/goals-progress.model';
import {PROJECTION_DISCLOSURE, ProjectionUtils} from './projection.utils';

/** Mirrors the backend's `Projection.MinCompleteMonths`: below it there is no savings pace to judge by. */
const MIN_COMPLETE_MONTHS = 3;
const WHOLE_UNITS = {maxFractionDigits: 0} as const;
const DATE_FORMATTER = new Intl.DateTimeFormat(APP_LOCALE, {
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

export class GoalsProgressUtils {
  /** "Sep 2028" from an ISO date. */
  public static dateLabel(isoDate: string): string {
    return DATE_FORMATTER.format(new Date(`${isoDate}T00:00:00Z`));
  }

  /** Every goal as the card draws it, in the order the IPS holds them. */
  public static rows(progress: GoalsProgress): GoalRowView[] {
    return progress.goals.map(goal => GoalsProgressUtils.row(goal, progress));
  }

  /** The IPS contribution plan beside what has actually been saved; null when the IPS has no plan. */
  public static planLine(progress: GoalsProgress): Nullable<string> {
    const plan = progress.contributionPlan;
    if (!plan) {
      return null;
    }
    const money = (amount: Nullable<number>): string =>
      MoneyUtils.format(amount, progress.baseCurrency, WHOLE_UNITS);
    const monthlyNote =
      plan.plannedMonthly != null && plan.cadence.trim().toLowerCase() !== 'monthly'
        ? ` (about ${money(plan.plannedMonthly)} a month)`
        : '';
    const planned = `Your IPS plans ${money(plan.plannedAmount)} ${plan.cadence}${monthlyNote}.`;

    if (plan.reason === GOALS_PROGRESS_REASONS.unrecognizedCadence) {
      return `${planned} That cadence can't be turned into a monthly figure to compare.`;
    }
    if (plan.actualMonthlySavings == null || plan.difference == null) {
      return `${planned} Your savings pace needs at least ${MIN_COMPLETE_MONTHS} complete months to compare.`;
    }
    const saved = `You have been saving about ${money(plan.actualMonthlySavings)} a month (your median net)`;
    const gap = Math.abs(plan.difference);
    if (Math.round(gap) === 0) {
      return `${planned} ${saved}, in line with the plan.`;
    }
    return `${planned} ${saved}, ${money(gap)} ${plan.difference > 0 ? 'more' : 'less'} than planned.`;
  }

  /** What every goal is measured against and the arithmetic behind the figures (C8): spelled out, not a forecast. */
  public static assumptions(progress: GoalsProgress): string {
    const money = (amount: number): string =>
      MoneyUtils.format(amount, progress.baseCurrency, WHOLE_UNITS);
    const returns = progress.realAnnualReturnRange;
    const savings =
      progress.monthlySavings == null
        ? ' and not enough history for a savings pace'
        : ` plus ${money(progress.monthlySavings)} a month saved (your median over ` +
          `${progress.completeMonths} complete months)`;
    const start =
      progress.currentNetWorth == null
        ? 'There is no net-worth snapshot yet.'
        : `Today: ${money(progress.currentNetWorth)} net worth${savings}.`;
    return (
      `${GOALS_PROGRESS_BASIS} ${start} Growth is assumed at a ` +
      `${PercentUtils.fromFraction(returns.central)}% real (after-inflation) annual return; the ` +
      `range uses ${ProjectionUtils.returnRange(returns)}. In today's money. ` +
      `${PROJECTION_DISCLOSURE} Context for your own planning, not advice.`
    );
  }

  private static row(goal: GoalProgress, progress: GoalsProgress): GoalRowView {
    return {
      label: goal.label,
      priority: `${goal.priority} priority`,
      trackLabel: GOAL_TRACK_LABEL[goal.onTrack],
      trackVariant: GOAL_TRACK_VARIANT[goal.onTrack],
      target: GoalsProgressUtils.target(goal, progress.baseCurrency),
      monthsLeft: GoalsProgressUtils.monthsLeft(goal.monthsLeft),
      required: GoalsProgressUtils.required(goal, progress.baseCurrency),
      projected: GoalsProgressUtils.projected(goal, progress.baseCurrency),
      note: GoalsProgressUtils.note(goal, progress),
    };
  }

  private static target(goal: GoalProgress, currency: string): string {
    const date = goal.targetDate ? GoalsProgressUtils.dateLabel(goal.targetDate) : null;
    if (goal.targetAmount == null || goal.targetAmount <= 0) {
      return date ? `No target amount, due ${date}` : 'No target amount';
    }
    const amount = MoneyUtils.format(goal.targetAmount, currency, WHOLE_UNITS);
    return date ? `${amount} by ${date}` : `${amount}, no target date`;
  }

  private static monthsLeft(months: Nullable<number>): Nullable<string> {
    if (months == null) {
      return null;
    }
    if (months === 0) {
      return 'date reached';
    }
    return `${months} ${months === 1 ? 'month' : 'months'} left`;
  }

  private static required(goal: GoalProgress, currency: string): Nullable<string> {
    const range = goal.requiredMonthlySaving;
    if (!range) {
      return null;
    }
    if (range.high === 0) {
      return goal.alreadyReached
        ? 'Already at or above this target; no further saving needed.'
        : 'Your balance growing on its own gets there; no further saving needed.';
    }
    const central = MoneyUtils.format(range.central, currency, WHOLE_UNITS);
    const spread = ProjectionUtils.moneyRange(range, currency);
    return spread === central
      ? `Needs about ${central} a month.`
      : `Needs about ${central} a month (${spread} across the return range).`;
  }

  private static projected(goal: GoalProgress, currency: string): Nullable<string> {
    const range = goal.projectedAtTargetDate;
    if (!range) {
      return null;
    }
    const central = MoneyUtils.format(range.central, currency, WHOLE_UNITS);
    return `At your current pace you would reach about ${central} by then (${ProjectionUtils.moneyRange(range, currency)}).`;
  }

  /** Why a verdict or a figure is missing, or what would give the goal one; null when nothing needs saying. */
  private static note(goal: GoalProgress, progress: GoalsProgress): Nullable<string> {
    switch (goal.reason) {
      case GOALS_PROGRESS_REASONS.targetDatePassed:
        return 'The target date has passed and net worth is below the target.';
      case GOALS_PROGRESS_REASONS.noSnapshot:
        return 'No net-worth snapshot yet, so this goal cannot be measured.';
      case GOALS_PROGRESS_REASONS.insufficientMonths:
        return (
          `Judging your savings pace needs at least ${MIN_COMPLETE_MONTHS} complete months of ` +
          `transactions (${progress.completeMonths} so far).`
        );
      default:
        break;
    }
    if (goal.onTrack === 'NoTargetDate') {
      return goal.alreadyReached
        ? 'Net worth is already at or above the target amount; add a target date to track it.'
        : 'Add a target date to see what saving it needs.';
    }
    return goal.onTrack === 'NoTargetAmount' ? 'Add a target amount to track this goal.' : null;
  }
}
