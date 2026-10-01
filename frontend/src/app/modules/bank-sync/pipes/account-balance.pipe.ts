import {Pipe, type PipeTransform} from '@angular/core';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';

export interface FormattedBalance {
  native: string;
  usd: Nullable<string>;
  /** A liability: the amount is what the user owes, so the row reads "Owes …" in the error tone. */
  owed: boolean;
}

// `AccountBalanceMath.IsLiability` on the backend: only credit accounts are liabilities.
const LIABILITY_ACCOUNT_TYPE = 'credit';

@Pipe({name: 'accountBalance'})
export class AccountBalancePipe implements PipeTransform {
  public transform({
    accountType,
    currentBalance,
    currency,
    balanceInBaseCurrency,
  }: AccountBalanceItem): FormattedBalance {
    const owed = accountType.toLowerCase() === LIABILITY_ACCOUNT_TYPE;
    const native = owed
      ? MoneyUtils.formatOwed(currentBalance, currency)
      : MoneyUtils.format(currentBalance, currency);

    if (
      currency === 'USD' ||
      balanceInBaseCurrency === null ||
      balanceInBaseCurrency === currentBalance
    ) {
      return {native, usd: null, owed};
    }

    return {native, usd: MoneyUtils.formatEquivalent(balanceInBaseCurrency), owed};
  }
}
