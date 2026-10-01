import {Pipe, type PipeTransform} from '@angular/core';

import {MoneyUtils} from '../utils/money.utils';

@Pipe({name: 'netBalance'})
export class NetBalancePipe implements PipeTransform {
  public transform(value: number | null | undefined, currency?: string | null): string {
    return MoneyUtils.formatNetBalance(value, currency);
  }
}
