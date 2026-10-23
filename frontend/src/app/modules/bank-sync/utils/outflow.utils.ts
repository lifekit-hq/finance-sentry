import {type Nullable} from '@lifekit-hq/ui';

interface OutflowScope {
  accountId: Nullable<string>;
  from: Nullable<string>;
  to: Nullable<string>;
}

export class OutflowUtils {
  /** Whether a figure was measured for exactly this account and date range. */
  public static sameScope(a: OutflowScope, b: OutflowScope): boolean {
    return a.accountId === b.accountId && a.from === b.from && a.to === b.to;
  }
}
