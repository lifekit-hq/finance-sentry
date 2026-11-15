import {Pipe, type PipeTransform} from '@angular/core';

import {type ThesisInvalidationTrigger} from '../models/thesis/thesis.model';
import {ThesisTriggerUtils} from '../utils/thesis-trigger.utils';

@Pipe({name: 'triggerSentence'})
export class TriggerSentencePipe implements PipeTransform {
  public transform(trigger: ThesisInvalidationTrigger): string {
    return ThesisTriggerUtils.describe(trigger);
  }
}
