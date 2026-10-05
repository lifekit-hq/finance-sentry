import {Pipe, type PipeTransform} from '@angular/core';

import {type Alert} from '../models/alert/alert.model';
import {AlertStatusUtils} from '../utils/alert-status.utils';

@Pipe({name: 'alertCaption'})
export class AlertCaptionPipe implements PipeTransform {
  public transform(item: Pick<Alert, 'isResolved' | 'occurrenceCount'>): Nullable<string> {
    return AlertStatusUtils.caption(item);
  }
}
