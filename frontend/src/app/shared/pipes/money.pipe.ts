import {Pipe, type PipeTransform} from '@angular/core';

import {type MoneyFormatOptions, MoneyUtils} from '../utils/money.utils';

@Pipe({name: 'money'})
export class MoneyPipe implements PipeTransform {
  public transform(
    value: number | null | undefined,
    currency?: string | null,
    options?: MoneyFormatOptions
  ): string {
    return MoneyUtils.format(value, currency, options);
  }
}
