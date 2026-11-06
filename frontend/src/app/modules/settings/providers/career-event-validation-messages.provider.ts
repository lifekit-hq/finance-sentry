import {type Provider} from '@angular/core';
import {
  DEFAULT_VALIDATION_MESSAGES,
  VALIDATION_MESSAGES,
  type ValidationMessageFn,
} from '@lifekit-hq/ui';

interface MaxDateParams {
  max: string;
}

/** Wording for the `min` / `max` keys the career event validators set, which the library's defaults do not word. */
export const CAREER_EVENT_VALIDATION_MESSAGES: Record<string, ValidationMessageFn> = {
  ...DEFAULT_VALIDATION_MESSAGES,
  min: () => 'Enter an amount above zero.',
  max: (params?: unknown) => `Choose a date no later than ${(params as MaxDateParams).max}.`,
};

/** Component-scoped: only the career event dialog needs these keys. */
export function provideCareerEventValidationMessages(): Provider {
  return {provide: VALIDATION_MESSAGES, useValue: CAREER_EVENT_VALIDATION_MESSAGES};
}
