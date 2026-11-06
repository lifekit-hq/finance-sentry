import {FormControl} from '@angular/forms';
import {describe, expect, it} from 'vitest';

import {type CareerEventKind} from '../models/career-event/career-event.model';
import {careerEventAmount} from './career-event-amount.validator';

function check(kind: CareerEventKind, value: number | string | null) {
  return careerEventAmount(() => kind)(new FormControl<number | string | null>(value));
}

describe('careerEventAmount', () => {
  it('requires an amount', () => {
    expect(check('OneOffIn', null)).toEqual({required: true});
    expect(check('IncomeStep', null)).toEqual({required: true});
  });

  it('accepts a negative income change but not a zero one', () => {
    expect(check('IncomeStep', -300)).toBeNull();
    expect(check('IncomeStep', 300)).toBeNull();
    expect(check('IncomeStep', 0)).toEqual({required: true});
  });

  it('wants a positive amount for a one-off or a leave', () => {
    expect(check('OneOffOut', 50)).toBeNull();
    expect(check('Leave', 1000)).toBeNull();
    expect(check('OneOffIn', -1)).toEqual({min: {min: 0, actual: -1}});
    expect(check('Leave', 0)).toEqual({min: {min: 0, actual: 0}});
  });

  it('reads the text cmn-input emits as a number', () => {
    expect(check('OneOffIn', '900')).toBeNull();
    expect(check('OneOffIn', ' 12.5 ')).toBeNull();
    expect(check('IncomeStep', '-300')).toBeNull();
    expect(check('IncomeStep', '0')).toEqual({required: true});
    expect(check('Leave', '-5')).toEqual({min: {min: 0, actual: -5}});
    expect(check('OneOffIn', '')).toEqual({required: true});
    expect(check('OneOffIn', 'abc')).toEqual({required: true});
  });
});
