import {Pipe, type PipeTransform} from '@angular/core';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {PositionUtils} from '../utils/position.utils';

@Pipe({name: 'positionValue'})
export class PositionValuePipe implements PipeTransform {
  public transform(account: AccountBalanceItem, baseCurrency: string): string {
    return PositionUtils.value(account, baseCurrency);
  }
}
