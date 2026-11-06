import {describe, expect, it} from 'vitest';

import {CAREER_EVENT_VALIDATION_MESSAGES} from './career-event-validation-messages.provider';

describe('CAREER_EVENT_VALIDATION_MESSAGES', () => {
  it('words the amount floor and the date horizon, and keeps the library defaults', () => {
    expect(CAREER_EVENT_VALIDATION_MESSAGES['min']()).toBe('Enter an amount above zero.');
    expect(CAREER_EVENT_VALIDATION_MESSAGES['max']({max: '2076-11-06', actual: '2077-01-01'})).toBe(
      'Choose a date no later than 2076-11-06.'
    );
    expect(CAREER_EVENT_VALIDATION_MESSAGES['required']()).toBe('This field is required.');
  });
});
