import {Pipe, type PipeTransform} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {InstitutionLogoUtils} from '../utils/institution-logo.utils';

@Pipe({name: 'institutionLogo'})
export class InstitutionLogoPipe implements PipeTransform {
  public transform(name: Nullable<string>, provider?: Nullable<string>): Nullable<string> {
    return InstitutionLogoUtils.faviconUrl(provider ?? null, name ?? null);
  }
}
