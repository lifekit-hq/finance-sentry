import {Pipe, type PipeTransform} from '@angular/core';

import {EventTitleUtils} from '../utils/event-title.utils';

@Pipe({name: 'eventTitle'})
export class EventTitlePipe implements PipeTransform {
  public transform(title: string, kindLabel: string, subject: string): string {
    return EventTitleUtils.rowTitle(title, kindLabel, subject);
  }
}
