import {type AbstractControl, type ValidationErrors, type ValidatorFn} from '@angular/forms';

import {PercentUtils} from '../../../shared/utils/percent.utils';

/** Error key a `percentRange` validator sets on a control holding empty, non-numeric or out-of-range text. */
export const PERCENT_RANGE_ERROR = 'percentRange';

/** Typed percent text must be a number inside `[minPercent, maxPercent]`; empty counts as invalid. */
export function percentRange(minPercent: number, maxPercent: number): ValidatorFn {
  return (control: AbstractControl<string>): ValidationErrors | null =>
    PercentUtils.toFraction(control.value ?? '', minPercent, maxPercent) === null
      ? {[PERCENT_RANGE_ERROR]: {min: minPercent, max: maxPercent}}
      : null;
}
