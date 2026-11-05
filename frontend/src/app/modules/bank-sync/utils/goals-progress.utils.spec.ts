import {describe, expect, it} from 'vitest';

import {
  type ContributionPlanComparison,
  type GoalProgress,
  type GoalsProgress,
} from '../models/goals-progress/goals-progress.model';
import {GoalsProgressUtils} from './goals-progress.utils';

function goal(overrides: Partial<GoalProgress> = {}): GoalProgress {
  return {
    label: 'House deposit',
    targetAmount: 130_000,
    targetDate: '2028-09-26',
    priority: 'high',
    monthsLeft: 24,
    requiredMonthlySaving: {low: 700, central: 785, high: 900},
    projectedAtTargetDate: {low: 132_000, central: 135_000, high: 138_000},
    alreadyReached: false,
    onTrack: 'OnTrack',
    reason: null,
    ...overrides,
  };
}

function plan(overrides: Partial<ContributionPlanComparison> = {}): ContributionPlanComparison {
  return {
    plannedAmount: 500,
    cadence: 'monthly',
    plannedMonthly: 500,
    actualMonthlySavings: 1_000,
    difference: 500,
    reason: null,
    ...overrides,
  };
}

function progress(overrides: Partial<GoalsProgress> = {}): GoalsProgress {
  return {
    status: 'Shown',
    currentNetWorth: 100_000,
    monthlySavings: 1_000,
    completeMonths: 6,
    goals: [goal()],
    contributionPlan: null,
    realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    basis: 'basis',
    disclosure: 'Hypothetical, not a guarantee.',
    ...overrides,
  };
}

describe('GoalsProgressUtils', () => {
  describe('dateLabel', () => {
    it('writes month and year from an ISO date', () => {
      expect(GoalsProgressUtils.dateLabel('2028-09-26')).toBe('Sept 2028');
    });
  });

  describe('rows', () => {
    it('writes a tracked goal out in full', () => {
      const [row] = GoalsProgressUtils.rows(progress());
      expect(row?.label).toBe('House deposit');
      expect(row?.priority).toBe('high priority');
      expect(row?.trackLabel).toBe('On track');
      expect(row?.trackVariant).toBe('success');
      expect(row?.target).toBe('$130,000 by Sept 2028');
      expect(row?.monthsLeft).toBe('24 months left');
      expect(row?.required).toBe(
        'Needs about $785 a month ($700 to $900 across the return range).'
      );
      expect(row?.projected).toBe(
        'At your current pace you would reach about $135,000 by then ($132,000 to $138,000).'
      );
      expect(row?.note).toBeNull();
    });

    it('keeps the IPS order', () => {
      const rows = GoalsProgressUtils.rows(
        progress({goals: [goal({label: 'Retire'}), goal({label: 'Car'})]})
      );
      expect(rows.map(r => r.label)).toEqual(['Retire', 'Car']);
    });

    it('states amounts in the base currency', () => {
      const [row] = GoalsProgressUtils.rows(progress({baseCurrency: 'EUR'}));
      expect(row?.target).toBe('€130,000 by Sept 2028');
    });

    it('marks a goal that is behind with a warning, not an error', () => {
      const [row] = GoalsProgressUtils.rows(progress({goals: [goal({onTrack: 'Behind'})]}));
      expect(row?.trackLabel).toBe('Behind');
      expect(row?.trackVariant).toBe('warning');
    });

    it('uses the singular for one month left and says a reached date is reached', () => {
      const one = GoalsProgressUtils.rows(progress({goals: [goal({monthsLeft: 1})]}));
      const zero = GoalsProgressUtils.rows(progress({goals: [goal({monthsLeft: 0})]}));
      expect(one[0]?.monthsLeft).toBe('1 month left');
      expect(zero[0]?.monthsLeft).toBe('date reached');
    });

    it('collapses a required range with no spread to one amount', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({goals: [goal({requiredMonthlySaving: {low: 785, central: 785, high: 785}})]})
      );
      expect(row?.required).toBe('Needs about $785 a month.');
    });

    it('says no further saving is needed when the balance is already past the target', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          goals: [
            goal({requiredMonthlySaving: {low: 0, central: 0, high: 0}, alreadyReached: true}),
          ],
        })
      );
      expect(row?.required).toBe('Already at or above this target; no further saving needed.');
    });

    it('says the balance gets there on its own when no saving is needed but it is not there yet', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({goals: [goal({requiredMonthlySaving: {low: 0, central: 0, high: 0}})]})
      );
      expect(row?.required).toBe(
        'Your balance growing on its own gets there; no further saving needed.'
      );
    });

    it('asks for a target date instead of claiming a verdict', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          goals: [
            goal({
              targetDate: null,
              monthsLeft: null,
              requiredMonthlySaving: null,
              projectedAtTargetDate: null,
              onTrack: 'NoTargetDate',
            }),
          ],
        })
      );
      expect(row?.trackLabel).toBe('No target date');
      expect(row?.target).toBe('$130,000, no target date');
      expect(row?.required).toBeNull();
      expect(row?.projected).toBeNull();
      expect(row?.note).toBe('Add a target date to see what saving it needs.');
    });

    it('notes that a goal without a date is already met', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          goals: [goal({targetDate: null, onTrack: 'NoTargetDate', alreadyReached: true})],
        })
      );
      expect(row?.note).toContain('already at or above the target amount');
    });

    it('asks for a target amount, keeping the date it has', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          goals: [
            goal({targetAmount: null, requiredMonthlySaving: null, onTrack: 'NoTargetAmount'}),
          ],
        })
      );
      expect(row?.trackLabel).toBe('No target amount');
      expect(row?.target).toBe('No target amount, due Sept 2028');
      expect(row?.note).toBe('Add a target amount to track this goal.');
    });

    it('names the missing amount and date together', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({goals: [goal({targetAmount: null, targetDate: null, onTrack: 'NoTargetAmount'})]})
      );
      expect(row?.target).toBe('No target amount');
    });

    it('explains a passed target date', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          goals: [
            goal({
              monthsLeft: 0,
              requiredMonthlySaving: null,
              projectedAtTargetDate: null,
              onTrack: 'Behind',
              reason: 'TARGET_DATE_PASSED',
            }),
          ],
        })
      );
      expect(row?.note).toBe('The target date has passed and net worth is below the target.');
    });

    it('explains a target date too far out to project', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          goals: [
            goal({
              requiredMonthlySaving: null,
              projectedAtTargetDate: null,
              onTrack: 'NoTargetDate',
              reason: 'TARGET_DATE_TOO_FAR',
            }),
          ],
        })
      );
      expect(row?.note).toBe('The target date is too far out to project; set a nearer date.');
    });

    it('explains missing history, with the months so far', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          completeMonths: 2,
          goals: [
            goal({
              projectedAtTargetDate: null,
              onTrack: 'InsufficientHistory',
              reason: 'INSUFFICIENT_MONTHS',
            }),
          ],
        })
      );
      expect(row?.trackLabel).toBe('Not enough history');
      expect(row?.required).not.toBeNull();
      expect(row?.projected).toBeNull();
      expect(row?.note).toContain('at least 3 complete months');
      expect(row?.note).toContain('2 so far');
    });

    it('explains a missing snapshot', () => {
      const [row] = GoalsProgressUtils.rows(
        progress({
          goals: [
            goal({
              requiredMonthlySaving: null,
              projectedAtTargetDate: null,
              onTrack: 'InsufficientHistory',
              reason: 'NO_SNAPSHOT',
            }),
          ],
        })
      );
      expect(row?.note).toContain('No net-worth snapshot yet');
    });
  });

  describe('planLine', () => {
    it('is null when the IPS has no plan', () => {
      expect(GoalsProgressUtils.planLine(progress())).toBeNull();
    });

    it('compares a plan with a bigger measured savings pace', () => {
      const line = GoalsProgressUtils.planLine(progress({contributionPlan: plan()}));
      expect(line).toBe(
        'Your IPS plans $500 monthly. You have been saving about $1,000 a month (your median net), $500 more than planned.'
      );
    });

    it('compares a plan with a smaller measured savings pace', () => {
      const line = GoalsProgressUtils.planLine(
        progress({contributionPlan: plan({actualMonthlySavings: 300, difference: -200})})
      );
      expect(line).toContain('$200 less than planned.');
    });

    it('says a pace within a unit of the plan is in line with it', () => {
      const line = GoalsProgressUtils.planLine(
        progress({contributionPlan: plan({actualMonthlySavings: 500.2, difference: 0.2})})
      );
      expect(line).toContain('in line with the plan.');
    });

    it('gives the monthly equivalent of a non-monthly cadence', () => {
      const line = GoalsProgressUtils.planLine(
        progress({
          contributionPlan: plan({plannedAmount: 100, cadence: 'weekly', plannedMonthly: 433.33}),
        })
      );
      expect(line).toContain('Your IPS plans $100 weekly (about $433 a month).');
    });

    it('does not guess at a cadence it cannot convert', () => {
      const line = GoalsProgressUtils.planLine(
        progress({
          contributionPlan: plan({
            plannedAmount: 100,
            cadence: 'whenever',
            plannedMonthly: null,
            difference: null,
            reason: 'UNRECOGNIZED_CADENCE',
          }),
        })
      );
      expect(line).toBe(
        "Your IPS plans $100 whenever. That cadence can't be turned into a monthly figure to compare."
      );
    });

    it('waits for history before comparing', () => {
      const line = GoalsProgressUtils.planLine(
        progress({
          contributionPlan: plan({
            actualMonthlySavings: null,
            difference: null,
            reason: 'INSUFFICIENT_MONTHS',
          }),
        })
      );
      expect(line).toContain('needs at least 3 complete months to compare');
    });
  });

  describe('assumptions', () => {
    it('says what goals are measured against and spells out every input', () => {
      const text = GoalsProgressUtils.assumptions(progress());
      expect(text).toContain('whole net worth plus your savings');
      expect(text).toContain('not against money set aside for it');
      expect(text).toContain('Today: $100,000 net worth plus $1,000 a month saved');
      expect(text).toContain('median over 6 complete months');
      expect(text).toContain('5% real (after-inflation) annual return');
      expect(text).toContain('2% to 8%');
      expect(text).toContain('Hypothetical, not a guarantee.');
      expect(text).toContain('not advice');
    });

    it('says so when there is no savings pace', () => {
      const text = GoalsProgressUtils.assumptions(progress({monthlySavings: null}));
      expect(text).toContain('$100,000 net worth and not enough history for a savings pace');
    });

    it('says so when there is no snapshot', () => {
      const text = GoalsProgressUtils.assumptions(
        progress({currentNetWorth: null, monthlySavings: null})
      );
      expect(text).toContain('There is no net-worth snapshot yet.');
    });
  });
});
