import {Pipe, type PipeTransform} from '@angular/core';

import {EventTitleUtils} from '../utils/event-title.utils';

@Pipe({name: 'eventTitle'})
export class EventTitlePipe implements PipeTransform {
  public transform(title: string, kindLabel: string): string {
    return EventTitleUtils.stripKindPrefix(title, kindLabel);
  }
}
