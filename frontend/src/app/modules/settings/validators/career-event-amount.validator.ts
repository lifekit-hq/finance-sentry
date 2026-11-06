import {type AbstractControl, type ValidationErrors, type ValidatorFn} from '@angular/forms';

import {type CareerEventKind} from '../models/career-event/career-event.model';

/**
 * An income change must move income by something (it may be negative, a pay cut); every other kind needs an amount
 * above zero. Empty reads as `required`, a non-positive one-off or leave as `min`, the keys the form field words.
 * `cmn-input` emits the typed text, so a string value is read as a number the way `positiveAmount` does.
 */
export function careerEventAmount(kind: () => CareerEventKind): ValidatorFn {
  return (control: AbstractControl<number | string | null>): ValidationErrors | null => {
    const raw = control.value;
    const text = raw === null ? '' : String(raw).trim();
    const value = Number(text);
    if (text === '' || !Number.isFinite(value)) {
      return {required: true};
    }
    if (kind() === 'IncomeStep') {
      return value === 0 ? {required: true} : null;
    }
    return value > 0 ? null : {min: {min: 0, actual: value}};
  };
}
