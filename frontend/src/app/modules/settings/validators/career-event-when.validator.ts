import {type AbstractControl, type ValidationErrors, type ValidatorFn} from '@angular/forms';
import {type DateRange} from '@lifekit-hq/ui';

import {type CareerEventKind} from '../models/career-event/career-event.model';

/**
 * The date of an event (`from`), and for a leave its last day (`to`, not before `from`). Any other kind ignores `to`.
 */
export function careerEventWhen(kind: () => CareerEventKind): ValidatorFn {
  return (control: AbstractControl<DateRange | null>): ValidationErrors | null => {
    const range = control.value;
    if (!range?.from) {
      return {required: true};
    }
    if (kind() === 'Leave' && (!range.to || range.to < range.from)) {
      return {required: true};
    }
    return null;
  };
}
