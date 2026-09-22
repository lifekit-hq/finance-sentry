import {Pipe, type PipeTransform} from '@angular/core';

import {EventDayUtils} from '../utils/event-day.utils';

@Pipe({name: 'eventDayLabel'})
export class EventDayLabelPipe implements PipeTransform {
  public transform(isoDate: string, today: string = EventDayUtils.toIsoDate(new Date())): string {
    return EventDayUtils.dayLabel(isoDate, today);
  }
}
