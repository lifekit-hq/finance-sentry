import {MoneyUtils} from '../../../shared/utils/money.utils';
import {PercentUtils} from '../../../shared/utils/percent.utils';
import {type FireProjection} from '../models/fire/fire.model';

const MONTHS_PER_YEAR = 12;
const FULL_PERCENT = 100;
const DATE_FORMATTER = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

export class FireTileUtils {
  /** "Mar 2041" from an ISO date. */
  public static formatDate(isoDate: string): string {
    return DATE_FORMATTER.format(new Date(`${isoDate}T00:00:00Z`));
  }

  /** "6 years 3 months", "1 year", "8 months" — whole months, rounded up like the date. */
  public static formatDuration(months: number): string {
    const total = Math.max(0, Math.ceil(months));
    if (total === 0) {
      return 'less than a month';
    }
    const years = Math.floor(total / MONTHS_PER_YEAR);
    const rest = total % MONTHS_PER_YEAR;
    const parts: string[] = [];
    if (years > 0) {
      parts.push(`${years} ${years === 1 ? 'year' : 'years'}`);
    }
    if (rest > 0) {
      parts.push(`${rest} ${rest === 1 ? 'month' : 'months'}`);
    }
    return parts.join(' ');
  }

  /** How far net worth is toward the target, as a whole percent clamped to 0–100. */
  public static progressPercent(currentNetWorth: number, target: number): number {
    if (target <= 0) {
      return 0;
    }
    return Math.min(
      FULL_PERCENT,
      Math.max(0, Math.round((currentNetWorth / target) * FULL_PERCENT))
    );
  }

  /** One sentence for the headline, per state — never a bare number to interpret. */
  public static headline(fire: FireProjection): string {
    switch (fire.status) {
      case 'Projected':
        return fire.projectedDate
          ? `Financial independence around ${FireTileUtils.formatDate(fire.projectedDate)}`
          : 'Financial independence date unavailable';
      case 'AlreadyReached':
        return 'You have reached your financial independence target';
      case 'NotSaving':
        return 'No date yet — nothing is being saved at the current rate';
      case 'InsufficientHistory':
        return '';
    }
  }

  /** The runway under the headline, only while a date exists. */
  public static runway(fire: FireProjection): Nullable<string> {
    return fire.status === 'Projected' && fire.monthsToFire !== null
      ? `About ${FireTileUtils.formatDuration(fire.monthsToFire)} from now`
      : null;
  }

  /** Every input and both assumptions, spelled out so the reader can check the arithmetic. */
  public static assumptions(fire: FireProjection): string {
    const swr = PercentUtils.fromFraction(fire.safeWithdrawalRate);
    const ret = PercentUtils.fromFraction(fire.realAnnualReturn);
    return (
      `Target ${MoneyUtils.format(fire.target, 'USD')} = a year of spending ` +
      `(${MoneyUtils.format(fire.annualSpend, 'USD')}, twelve times your median monthly spending ` +
      `over complete months) divided by a ${swr}% safe withdrawal rate. ` +
      `Assumes a ${ret}% real (after-inflation) annual return on your ` +
      `${MoneyUtils.format(fire.currentNetWorth, 'USD')} net worth and on future savings of ` +
      `${MoneyUtils.format(fire.monthlySavings, 'USD')} a month (your median over complete months), ` +
      'compounded monthly.'
    );
  }
}
