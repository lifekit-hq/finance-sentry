import {type AbstractControl, type ValidationErrors, type ValidatorFn} from '@angular/forms';

import {type CareerEventKind} from '../models/career-event/career-event.model';

/**
 * An income change must move income by something (it may be negative, a pay cut); every other kind needs an amount
 * above zero. Empty reads as `required`, a non-positive one-off or leave as `min`, the keys the form field words.
 */
export function careerEventAmount(kind: () => CareerEventKind): ValidatorFn {
  return (control: AbstractControl<number | null>): ValidationErrors | null => {
    const value = control.value;
    if (value === null || !Number.isFinite(value)) {
      return {required: true};
    }
    if (kind() === 'IncomeStep') {
      return value === 0 ? {required: true} : null;
    }
    return value > 0 ? null : {min: {min: 0, actual: value}};
  };
}
