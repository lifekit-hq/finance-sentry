import {type Nullable} from '@lifekit-hq/ui';

import {type MonthlyFlow} from '../models/dashboard/dashboard.model';
import {LedgerPeriodUtils} from './ledger-period.utils';

const MONTH_KEY_LENGTH = 7;

export class OutflowUtils {
  /**
   * Outflow, in the base currency, of the whole months a date range touches (`YYYY-MM` of `from`
   * through `to`); open-ended bounds reach the start / end of the history. The ledger's quick
   * periods are whole-month windows, so this is the dashboard's figure for them.
   */
  public static sumMonths(
    monthlyFlow: MonthlyFlow[],
    from: Nullable<string>,
    to: Nullable<string>
  ): number {
    const first = from?.slice(0, MONTH_KEY_LENGTH) ?? null;
    const last = to?.slice(0, MONTH_KEY_LENGTH) ?? null;
    return monthlyFlow
      .filter(r => (first === null || r.month >= first) && (last === null || r.month <= last))
      .reduce((sum, r) => sum + r.outflowUsd, 0);
  }

  /**
   * Whether the dashboard's month flow answers for this scope: every account, over all time or
   * a quick period (whole months). An account or a custom date range is summed from rows instead.
   */
  public static usesMonthFlow(scope: {
    accountId: Nullable<string>;
    from: Nullable<string>;
    to: Nullable<string>;
  }): boolean {
    const open = scope.from === null && scope.to === null;
    return (
      scope.accountId === null && (open || LedgerPeriodUtils.match(scope.from, scope.to) !== null)
    );
  }

  /** Whether a computed outflow was measured for exactly this account and date range. */
  public static sameScope(
    a: {accountId: Nullable<string>; from: Nullable<string>; to: Nullable<string>},
    b: {accountId: Nullable<string>; from: Nullable<string>; to: Nullable<string>}
  ): boolean {
    return a.accountId === b.accountId && a.from === b.from && a.to === b.to;
  }
}
