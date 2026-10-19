import {MoneyUtils} from '../../../shared/utils/money.utils';
import {PercentUtils} from '../../../shared/utils/percent.utils';
import {type NetWorthProjection, type ProjectionRange} from '../models/projection/projection.model';

// Cents on a twelve-month projection are noise.
const WHOLE_UNITS = {maxFractionDigits: 0} as const;

/** Every projection closes on this (C8): it is an illustration, not a forecast anyone guarantees. */
export const PROJECTION_DISCLOSURE = 'Hypothetical, not a guarantee.';

export class ProjectionUtils {
  /** "2% to 8%", or the single rate when the range has no spread. */
  public static returnRange(range: ProjectionRange): string {
    const low = PercentUtils.fromFraction(range.low);
    const high = PercentUtils.fromFraction(range.high);
    return low === high ? `${low}%` : `${low}% to ${high}%`;
  }

  /** "$22,300 to $23,100" in whole units, or the single amount when the range has no spread. */
  public static moneyRange(range: ProjectionRange, currency: string): string {
    const low = MoneyUtils.format(range.low, currency, WHOLE_UNITS);
    const high = MoneyUtils.format(range.high, currency, WHOLE_UNITS);
    return low === high ? low : `${low} to ${high}`;
  }

  /** The central twelve-month figure in whole units; an em dash when there is none. */
  public static netWorthCentral(projection: NetWorthProjection): string {
    return MoneyUtils.format(
      projection.projectedNetWorth?.central,
      projection.baseCurrency,
      WHOLE_UNITS
    );
  }

  /** The low-to-high twelve-month range; empty when there is none. */
  public static netWorthRange(projection: NetWorthProjection): string {
    return projection.projectedNetWorth
      ? ProjectionUtils.moneyRange(projection.projectedNetWorth, projection.baseCurrency)
      : '';
  }

  /** Every input and the return assumption, spelled out next to the figure (C8). */
  public static netWorthAssumptions(projection: NetWorthProjection): string {
    const money = (amount: number): string =>
      MoneyUtils.format(amount, projection.baseCurrency, WHOLE_UNITS);
    const returns = projection.realAnnualReturnRange;
    return (
      `Your ${money(projection.currentNetWorth)} net worth plus ` +
      `${money(projection.monthlySavings)} a month saved (your median over ` +
      `${projection.completeMonths} complete months), growing at a ` +
      `${PercentUtils.fromFraction(returns.central)}% real (after-inflation) annual return; ` +
      `the range uses ${ProjectionUtils.returnRange(returns)}. In today's money. ` +
      PROJECTION_DISCLOSURE
    );
  }
}
