import {Pipe, type PipeTransform} from '@angular/core';

import {MoneyUtils} from '../../../shared/utils/money.utils';
import {GlobalTransactionDto} from '../models/transaction/transaction.model';

type SignedAmountWithCurrency = Pick<
  GlobalTransactionDto,
  'amount' | 'transactionType' | 'currency'
>;

@Pipe({name: 'transactionAmount'})
export class TransactionAmountPipe implements PipeTransform {
  public transform<T extends SignedAmountWithCurrency>({
    transactionType,
    amount,
    currency,
  }: T): string {
    const sign = transactionType === 'credit' ? '+' : '-';
    return `${sign}${MoneyUtils.format(Math.abs(amount), currency)}`;
  }
}
