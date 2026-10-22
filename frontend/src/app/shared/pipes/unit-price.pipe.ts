import {Pipe, type PipeTransform} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {UnitPriceUtils} from '../utils/unit-price.utils';

@Pipe({name: 'unitPrice'})
export class UnitPricePipe implements PipeTransform {
  public transform(price: Nullable<number> | undefined, currency?: Nullable<string>): string {
    return UnitPriceUtils.format(price, currency);
  }
}
