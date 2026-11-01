import {type AbstractControl, type ValidationErrors, type ValidatorFn} from '@angular/forms';

/** Error key an `amountText` validator sets on a control holding empty, non-numeric or out-of-range text. */
export const AMOUNT_TEXT_ERROR = 'amountText';

/** Matches the backend's cap on a stored amount (1,000,000,000), which the validator keeps typed text under. */
export const AMOUNT_TEXT_MAX = 999_999_999;

/** Typed amount text (at most two decimals) from `min` (inclusive) to `AMOUNT_TEXT_MAX`; empty counts as invalid. */
export function amountText(min: number): ValidatorFn {
  return (control: AbstractControl<string>): ValidationErrors | null => {
    const text = (control.value ?? '').trim();
    const value = Number(text);
    const wellFormed = /^\d+(\.\d{1,2})?$/.test(text);
    return wellFormed && value >= min && value <= AMOUNT_TEXT_MAX
      ? null
      : {[AMOUNT_TEXT_ERROR]: {min, max: AMOUNT_TEXT_MAX}};
  };
}
