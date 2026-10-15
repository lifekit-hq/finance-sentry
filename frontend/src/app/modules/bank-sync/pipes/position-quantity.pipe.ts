import {Pipe, type PipeTransform} from '@angular/core';

import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {PositionUtils} from '../utils/position.utils';

@Pipe({name: 'positionQuantity'})
export class PositionQuantityPipe implements PipeTransform {
  public transform(account: AccountBalanceItem): string {
    return PositionUtils.quantity(account);
  }
}
