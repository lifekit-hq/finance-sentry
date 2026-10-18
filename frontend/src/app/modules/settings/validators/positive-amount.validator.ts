import {type AbstractControl, type ValidationErrors, type ValidatorFn} from '@angular/forms';

/** Error key a `positiveAmount` validator sets on a control holding empty, non-numeric or out-of-range text. */
export const POSITIVE_AMOUNT_ERROR = 'positiveAmount';

/** Typed amount text must be a number above zero and at most `max`; empty counts as invalid. */
export function positiveAmount(max: number): ValidatorFn {
  return (control: AbstractControl<string>): ValidationErrors | null => {
    const text = (control.value ?? '').trim();
    const value = Number(text);
    return text !== '' && Number.isFinite(value) && value > 0 && value <= max
      ? null
      : {[POSITIVE_AMOUNT_ERROR]: {max}};
  };
}
