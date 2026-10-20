import {MoneyUtils} from '../../../shared/utils/money.utils';
import {PercentUtils} from '../../../shared/utils/percent.utils';
import {type MonthlyIncomeLens} from '../models/income-lens/income-lens.model';
import {PROJECTION_DISCLOSURE} from './projection.utils';

// Cents on an income a total could pay are noise.
const WHOLE_UNITS = {maxFractionDigits: 0} as const;

export class IncomeLensUtils {
  /** The monthly income in whole units; an em dash when there is none. */
  public static monthlyIncome(lens: MonthlyIncomeLens): string {
    return MoneyUtils.format(lens.monthlyIncome, lens.baseCurrency, WHOLE_UNITS);
  }

  /** The total and the withdrawal rate it is drawn at, spelled out next to the figure (C8). */
  public static assumptions(lens: MonthlyIncomeLens): string {
    const netWorth = MoneyUtils.format(lens.netWorth, lens.baseCurrency, WHOLE_UNITS);
    const rate = PercentUtils.fromFraction(lens.safeWithdrawalRate);
    return (
      `Your ${netWorth} net worth drawn at ${rate}% a year, the safe withdrawal rate your ` +
      `financial-independence target uses. In today's money. ${PROJECTION_DISCLOSURE}`
    );
  }
}
