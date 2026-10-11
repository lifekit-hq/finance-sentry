import {Pipe, type PipeTransform} from '@angular/core';

import {DateUtils} from '../../../shared/utils/date.utils';
import {EventDayUtils} from '../utils/event-day.utils';

@Pipe({name: 'eventDayLabel'})
export class EventDayLabelPipe implements PipeTransform {
  public transform(isoDate: string, today: string = DateUtils.isoDateLocal(new Date())): string {
    return EventDayUtils.dayLabel(isoDate, today);
  }
}
