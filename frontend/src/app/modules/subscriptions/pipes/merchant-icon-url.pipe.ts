import {Pipe, type PipeTransform} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {MerchantIconUtils} from '../utils/merchant-icon.utils';

@Pipe({name: 'merchantIconUrl'})
export class MerchantIconUrlPipe implements PipeTransform {
  public transform(iconId: Nullable<number> | undefined): Nullable<string> {
    return MerchantIconUtils.url(iconId);
  }
}
