import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  FX_IMPACT_DEFAULT_ENABLED,
  FX_IMPACT_ENABLED_STORAGE_KEY,
  FX_IMPACT_FLAT_THRESHOLD,
} from '../constants/fx-impact/fx-impact.constants';
import {
  type FxImpactPlan,
  type FxImpactPlanView,
  type FxImpactTotals,
} from '../models/fx-impact/fx-impact.model';

const STORED_ON = '1';
const STORED_OFF = '0';

export class FxImpactUtils {
  /** Whether this device shows the insight; on when never chosen, unreadable or storage is unavailable. */
  public static readEnabled(): boolean {
    try {
      const stored = localStorage.getItem(FX_IMPACT_ENABLED_STORAGE_KEY);
      return stored === null ? FX_IMPACT_DEFAULT_ENABLED : stored === STORED_ON;
    } catch {
      return FX_IMPACT_DEFAULT_ENABLED;
    }
  }

  public static writeEnabled(enabled: boolean): void {
    try {
      localStorage.setItem(FX_IMPACT_ENABLED_STORAGE_KEY, enabled ? STORED_ON : STORED_OFF);
    } catch {
      // Storage blocked: the insight returns to its default on the next visit, nothing else depends on it.
    }
  }

  /** A plan with the display values the template binds: absolute amounts with a saved/extra flag each. */
  public static planView(plan: FxImpactPlan): FxImpactPlanView {
    return {
      plan,
      chart: plan.rates.map(point => ({
        label: point.date,
        value: point.unitsPerBase,
        time: new Date(point.date).getTime(),
      })),
      paidSaved: plan.paidSaving >= 0,
      paidAmount: Math.abs(plan.paidSaving),
      remainingSaved: (plan.remainingSaving ?? 0) >= 0,
      remainingAmount: plan.remainingSaving === null ? null : Math.abs(plan.remainingSaving),
    };
  }

  /**
   * The one aggregate sentence: what rate moves have saved (or cost) so far and on the payments left, in the
   * base currency. A zero part is left out rather than stated.
   */
  public static summaryLine(totals: FxImpactTotals, baseCurrency: string): string {
    const money = (amount: number): string => MoneyUtils.format(Math.abs(amount), baseCurrency);
    const isFlat = (amount: number): boolean => Math.abs(amount) < FX_IMPACT_FLAT_THRESHOLD;

    if (isFlat(totals.paidSaving) && isFlat(totals.remainingSaving)) {
      return 'Exchange-rate moves have not changed what these plans cost you.';
    }

    const soFar = isFlat(totals.paidSaving)
      ? "not changed what you've paid so far"
      : totals.paidSaving > 0
        ? `saved you ${money(totals.paidSaving)} so far`
        : `cost you ${money(totals.paidSaving)} more so far`;

    const left = isFlat(totals.remainingSaving)
      ? ''
      : totals.remainingSaving > 0
        ? ` and will save ${money(totals.remainingSaving)} on the payments left`
        : ` and will cost ${money(totals.remainingSaving)} more on the payments left`;

    return `Rate moves have ${soFar}${left}.`;
  }
}
