import {Pipe, type PipeTransform} from '@angular/core';
import {type IconName, type Nullable} from '@lifekit-hq/ui';

import {MerchantIconUtils} from '../utils/merchant-icon.utils';

@Pipe({name: 'categoryIcon'})
export class CategoryIconPipe implements PipeTransform {
  public transform(category: Nullable<string> | undefined): Nullable<IconName> {
    return MerchantIconUtils.categoryIcon(category);
  }
}
