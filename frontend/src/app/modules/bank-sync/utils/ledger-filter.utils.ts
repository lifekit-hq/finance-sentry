import {type Nullable} from '@lifekit-hq/ui';

import {type LedgerFilter} from '../store/transaction-ledger/transaction-ledger.computed';

export class LedgerFilterUtils {
  /** Two filters ask the server for the same rows; an absent filter matches nothing. */
  public static same(a: Nullable<LedgerFilter>, b: Nullable<LedgerFilter>): boolean {
    return (
      a != null &&
      b != null &&
      a.accountId === b.accountId &&
      a.transactionType === b.transactionType &&
      a.categories.join() === b.categories.join() &&
      a.from === b.from &&
      a.to === b.to &&
      a.search === b.search
    );
  }
}
