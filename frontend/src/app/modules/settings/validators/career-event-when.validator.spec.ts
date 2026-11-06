import {FormControl} from '@angular/forms';
import {type DateRange} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {type CareerEventKind} from '../models/career-event/career-event.model';
import {careerEventWhen} from './career-event-when.validator';

function check(kind: CareerEventKind, value: DateRange | null) {
  return careerEventWhen(() => kind)(new FormControl<DateRange | null>(value));
}

describe('careerEventWhen', () => {
  it('requires a date', () => {
    expect(check('IncomeStep', null)).toEqual({required: true});
    expect(check('IncomeStep', {from: null, to: null})).toEqual({required: true});
  });

  it('ignores the end for a kind that has none', () => {
    expect(check('OneOffIn', {from: '2027-03-01', to: null})).toBeNull();
    expect(check('IncomeStep', {from: '2027-03-01', to: '2020-01-01'})).toBeNull();
  });

  it('needs a leave to end on or after its start', () => {
    expect(check('Leave', {from: '2027-06-01', to: null})).toEqual({required: true});
    expect(check('Leave', {from: '2027-06-01', to: '2027-05-31'})).toEqual({required: true});
    expect(check('Leave', {from: '2027-06-01', to: '2027-06-01'})).toBeNull();
    expect(check('Leave', {from: '2027-06-01', to: '2027-08-31'})).toBeNull();
  });
});
