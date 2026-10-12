import {Pipe, type PipeTransform} from '@angular/core';

import {FLOW_IN_TEXT_CLASS} from '../../../shared/constants/chart-colour/chart-colour.constants';
import {type GlobalTransactionDto} from '../models/transaction/transaction.model';

/** Credits read green, pending rows muted, everything else in the primary text colour. */
@Pipe({name: 'transactionAmountClass'})
export class TransactionAmountClassPipe implements PipeTransform {
  public transform(
    transaction: Pick<GlobalTransactionDto, 'transactionType' | 'isPending'>
  ): string {
    if (transaction.isPending) {
      return 'text-text-secondary';
    }
    return transaction.transactionType === 'credit' ? FLOW_IN_TEXT_CLASS : 'text-text-primary';
  }
}
