import {Pipe, type PipeTransform} from '@angular/core';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';

export interface FormattedBalance {
  native: string;
  usd: Nullable<string>;
}

@Pipe({name: 'accountBalance'})
export class AccountBalancePipe implements PipeTransform {
  public transform({
    currentBalance,
    currency,
    balanceInBaseCurrency,
  }: AccountBalanceItem): FormattedBalance {
    const native = MoneyUtils.format(currentBalance, currency);

    if (
      currency === 'USD' ||
      balanceInBaseCurrency === null ||
      balanceInBaseCurrency === currentBalance
    ) {
      return {native, usd: null};
    }

    return {native, usd: MoneyUtils.formatEquivalent(balanceInBaseCurrency)};
  }
}
