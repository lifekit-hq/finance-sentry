import {Pipe, type PipeTransform} from '@angular/core';

import {type Subscription} from '../models/subscription/subscription.model';
import {SubscriptionUtils} from '../utils/subscription.utils';

@Pipe({name: 'installmentProgress'})
export class InstallmentProgressPipe implements PipeTransform {
  public transform(
    item: Pick<Subscription, 'occurrenceCount' | 'termCount' | 'remainingPayments'>
  ): string {
    return SubscriptionUtils.installmentProgress(item);
  }
}
