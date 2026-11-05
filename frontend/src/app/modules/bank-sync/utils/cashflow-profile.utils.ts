import {type Nullable} from '@lifekit-hq/ui';

import {APP_LOCALE} from '../../../shared/constants/date/date.constants';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  CASHFLOW_PROFILE_MIN_MONTHS,
  CASHFLOW_PROFILE_REASONS,
} from '../constants/cashflow-profile/cashflow-profile.constants';
import {type CashflowProfile} from '../models/cashflow-profile/cashflow-profile.model';

const FULL_PERCENT = 100;
const RUNWAY_DIGITS = 1;
const MONTH_FORMATTER = new Intl.DateTimeFormat(APP_LOCALE, {
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

export class CashflowProfileUtils {
  /** "Mar 2026" from a "yyyy-MM" month key. */
  public static monthLabel(monthKey: string): string {
    const [year, month] = monthKey.split('-').map(Number);
    return MONTH_FORMATTER.format(new Date(Date.UTC(year ?? 0, (month ?? 1) - 1, 1)));
  }

  /** A pooled rate as a whole percent: 0.25 → "25%", -0.1 → "-10%"; a rate that rounds to zero is "0%". */
  public static rateLabel(rate: number): string {
    const percent = Math.round(rate * FULL_PERCENT);
    return `${percent === 0 ? 0 : percent}%`;
  }

  /** "6 months", "6.5 months", "1 month": runway to one decimal, the trailing ".0" dropped. */
  public static runwayLabel(months: number): string {
    const rounded = Number(months.toFixed(RUNWAY_DIGITS));
    return `${rounded} ${rounded === 1 ? 'month' : 'months'}`;
  }

  /** Why there is no runway figure, in words; null while there is one. */
  public static runwayNote(profile: CashflowProfile): Nullable<string> {
    if (profile.runwayMonths != null) {
      return null;
    }
    switch (profile.runwayReason) {
      case CASHFLOW_PROFILE_REASONS.noSnapshot:
        return 'No net-worth snapshot yet, so there is no cash to measure.';
      case CASHFLOW_PROFILE_REASONS.noCashSplit:
        return "Your latest snapshot doesn't split out cash, so runway can't be worked out.";
      case CASHFLOW_PROFILE_REASONS.noCash:
        return 'No cash in your latest snapshot to run on.';
      case CASHFLOW_PROFILE_REASONS.noBurn:
        return 'No spending in these months, so cash is not being used up.';
      default:
        return 'Runway is unavailable right now.';
    }
  }

  /** What the savings rate says, in words, so a negative one is not left to be interpreted. */
  public static rateNote(rate: number): string {
    return rate < 0 ? 'spending more than came in' : 'of income kept';
  }

  /** The empty state's body: what is missing and what would change it. */
  public static emptySubMessage(profile: Nullable<CashflowProfile>): string {
    if (profile?.reason === CASHFLOW_PROFILE_REASONS.noPositiveIncome) {
      return 'No income came in over the complete months so far, so a savings rate has nothing to measure against.';
    }
    const have = profile?.completeMonths ?? 0;
    return (
      `Burn, savings rate and runway need at least ${CASHFLOW_PROFILE_MIN_MONTHS} complete months of ` +
      `transactions (${have} so far). Sync your accounts and check back next month.`
    );
  }

  /** Every input spelled out so the reader can check the arithmetic; context, not a forecast or advice. */
  public static assumptions(profile: CashflowProfile): string {
    const money = (amount: Nullable<number>): string =>
      MoneyUtils.format(amount, profile.baseCurrency, {maxFractionDigits: 0});
    const window =
      profile.fromMonth && profile.toMonth
        ? `the ${profile.completeMonths} complete months from ` +
          `${CashflowProfileUtils.monthLabel(profile.fromMonth)} to ` +
          `${CashflowProfileUtils.monthLabel(profile.toMonth)}`
        : `the ${profile.completeMonths} complete months`;
    return (
      `A typical month is the median over ${window}: ${money(profile.incomeMonthlyMedian)} in, ` +
      `${money(profile.burnMonthlyMedian)} out. The savings rate is everything saved divided by ` +
      'everything earned over those months together, so one odd month cannot swing it. Runway is ' +
      "the cash in your latest snapshot divided by a typical month's spending; income is not " +
      'counted. Context for your own planning, not a forecast or advice.'
    );
  }
}
