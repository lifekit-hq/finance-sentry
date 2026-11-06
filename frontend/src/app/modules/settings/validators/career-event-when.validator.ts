import {type AbstractControl, type ValidationErrors, type ValidatorFn} from '@angular/forms';
import {type DateRange} from '@lifekit-hq/ui';

import {DateUtils} from '../../../shared/utils/date.utils';
import {CAREER_EVENT_MAX_YEARS_AHEAD} from '../constants/career-event/career-event.constants';
import {type CareerEventKind} from '../models/career-event/career-event.model';

/**
 * The date of an event (`from`), and for a leave its last day (`to`, not before `from`). Any other kind ignores `to`.
 * Neither date may be more than {@link CAREER_EVENT_MAX_YEARS_AHEAD} years ahead (reads as `max`).
 */
export function careerEventWhen(
  kind: () => CareerEventKind,
  now: () => Date = () => new Date()
): ValidatorFn {
  return (control: AbstractControl<DateRange | null>): ValidationErrors | null => {
    const range = control.value;
    if (!range?.from) {
      return {required: true};
    }
    const isLeave = kind() === 'Leave';
    if (isLeave && (!range.to || range.to < range.from)) {
      return {required: true};
    }
    const horizon = now();
    horizon.setUTCFullYear(horizon.getUTCFullYear() + CAREER_EVENT_MAX_YEARS_AHEAD);
    const latest = DateUtils.isoDateUtc(horizon);
    const last = isLeave && range.to ? range.to : range.from;
    return last > latest ? {max: {max: latest, actual: last}} : null;
  };
}
