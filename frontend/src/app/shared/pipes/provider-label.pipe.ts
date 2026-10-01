import {Pipe, type PipeTransform} from '@angular/core';

import {ProviderUtils} from '../utils/provider.utils';

@Pipe({name: 'providerLabel'})
export class ProviderLabelPipe implements PipeTransform {
  public transform(slug: string): string {
    return ProviderUtils.label(slug);
  }
}
