import {Pipe, type PipeTransform} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';

export interface FormattedBalance {
  native: string;
  equivalent: Nullable<string>;
  /** A liability: the stored amount is what the user owes, shown negative in the error tone. */
  owed: boolean;
}

// `AccountBalanceMath.IsLiability` on the backend: only credit accounts are liabilities.
const LIABILITY_ACCOUNT_TYPE = 'credit';

@Pipe({name: 'accountBalance'})
export class AccountBalancePipe implements PipeTransform {
  public transform(
    {
      accountType,
      currentBalance,
      currency,
      balanceInBaseCurrency,
      nativeCurrency,
      nativeValue,
    }: AccountBalanceItem,
    baseCurrency: string
  ): FormattedBalance {
    const hasNative = nativeValue !== null && nativeValue !== undefined;
    const amount = hasNative ? nativeValue : currentBalance;
    const shownCurrency = hasNative ? nativeCurrency : currency;
    const owed = accountType.toLowerCase() === LIABILITY_ACCOUNT_TYPE && amount > 0;
    const native = MoneyUtils.format(owed ? -amount : amount, shownCurrency);

    if (shownCurrency === baseCurrency || balanceInBaseCurrency === null) {
      return {native, equivalent: null, owed};
    }

    return {
      native,
      equivalent: MoneyUtils.formatEquivalent(
        owed ? -balanceInBaseCurrency : balanceInBaseCurrency,
        baseCurrency
      ),
      owed,
    };
  }
}
