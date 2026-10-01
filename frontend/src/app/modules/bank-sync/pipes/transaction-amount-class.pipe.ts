import {Pipe, type PipeTransform} from '@angular/core';

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
    return transaction.transactionType === 'credit' ? 'text-status-success' : 'text-text-primary';
  }
}
