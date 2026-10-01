import {Pipe, type PipeTransform} from '@angular/core';

import {AlertMessageUtils} from '../utils/alert-message.utils';

@Pipe({name: 'alertMessage'})
export class AlertMessagePipe implements PipeTransform {
  public transform(message: Nullable<string>): string {
    return AlertMessageUtils.roundNumbers(message);
  }
}
