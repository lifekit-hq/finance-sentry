import {FormControl} from '@angular/forms';
import {type DateRange} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {type CareerEventKind} from '../models/career-event/career-event.model';
import {careerEventWhen} from './career-event-when.validator';

const NOW = new Date('2026-11-06T12:00:00Z');

function check(kind: CareerEventKind, value: DateRange | null) {
  return careerEventWhen(
    () => kind,
    () => new Date(NOW)
  )(new FormControl<DateRange | null>(value));
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

  it('rejects a date more than 50 years ahead and accepts one on the horizon', () => {
    expect(check('IncomeStep', {from: '2076-11-07', to: null})).toEqual({
      max: {max: '2076-11-06', actual: '2076-11-07'},
    });
    expect(check('IncomeStep', {from: '2076-11-06', to: null})).toBeNull();
  });

  it('rejects a leave that ends past the horizon, even far past it', () => {
    expect(check('Leave', {from: '2027-06-01', to: '2076-11-07'})).toEqual({
      max: {max: '2076-11-06', actual: '2076-11-07'},
    });
    expect(check('Leave', {from: '2027-06-01', to: '9999-12-31'})).toEqual({
      max: {max: '2076-11-06', actual: '9999-12-31'},
    });
    expect(check('Leave', {from: '2027-06-01', to: '2076-11-06'})).toBeNull();
  });

  it('ignores the end past the horizon for a kind that has none', () => {
    expect(check('OneOffOut', {from: '2027-03-01', to: '9999-12-31'})).toBeNull();
  });
});
