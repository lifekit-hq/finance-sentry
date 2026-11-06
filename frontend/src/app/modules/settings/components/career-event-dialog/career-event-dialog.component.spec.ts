import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type CareerEvent} from '../../models/career-event/career-event.model';
import {FUTURE_LEAVE, FUTURE_RAISE} from '../../store/career-events/career-events.fixtures';
import {CareerEventDialogComponent} from './career-event-dialog.component';

function setup(event: CareerEvent | null = null) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: DialogRef, useValue: {close}},
      {provide: CMN_DIALOG_DATA, useValue: {event}},
    ],
  });
  return {
    component: TestBed.createComponent(CareerEventDialogComponent).componentInstance,
    close,
  };
}

describe('CareerEventDialogComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('starts a new event as an income change in dollars', () => {
    const {component} = setup();

    expect(component.isEdit).toBe(false);
    expect(component.form.getRawValue()).toEqual({
      kind: 'IncomeStep',
      label: '',
      amount: null,
      currency: 'USD',
      when: {from: null, to: null},
      note: '',
    });
    expect(component.isLeave()).toBe(false);
  });

  it('opens an edit with the event filled in', () => {
    const {component} = setup(FUTURE_LEAVE);

    expect(component.isEdit).toBe(true);
    expect(component.form.getRawValue()).toEqual({
      kind: 'Leave',
      label: 'Sabbatical',
      amount: 1200,
      currency: 'USD',
      when: {from: '2027-06-01', to: '2027-08-31'},
      note: 'Unpaid',
    });
    expect(component.isLeave()).toBe(true);
  });

  it('refuses an empty form and closes nothing', () => {
    const {component, close} = setup();

    component.submit();

    expect(close).not.toHaveBeenCalled();
    expect(component.form.controls.label.touched).toBe(true);
  });

  it('accepts a negative income change but refuses a zero one', () => {
    const {component, close} = setup();
    component.form.patchValue({label: 'Cut', amount: 0, when: {from: '2027-03-01', to: null}});

    component.submit();
    expect(close).not.toHaveBeenCalled();

    component.form.patchValue({amount: -300});
    component.submit();
    expect(close).toHaveBeenCalledWith(expect.objectContaining({kind: 'IncomeStep', amount: -300}));
  });

  it('re-checks the amount when the kind changes to one that needs a positive amount', () => {
    const {component} = setup();
    component.form.patchValue({amount: -300});
    expect(component.form.controls.amount.valid).toBe(true);

    component.form.controls.kind.setValue('OneOffIn');

    expect(component.form.controls.amount.valid).toBe(false);
  });

  it('needs a leave to carry an end date on or after its start', () => {
    const {component, close} = setup();
    component.form.patchValue({
      kind: 'Leave',
      label: 'Leave',
      amount: 1000,
      when: {from: '2027-06-01', to: null},
    });

    component.submit();
    expect(close).not.toHaveBeenCalled();

    component.form.patchValue({when: {from: '2027-06-01', to: '2027-08-31'}});
    component.submit();
    expect(close).toHaveBeenCalledWith(
      expect.objectContaining({kind: 'Leave', date: '2027-06-01', endDate: '2027-08-31'})
    );
  });

  it('sends a trimmed label and null for an empty note, and no end date for a kind that has none', () => {
    const {component, close} = setup();
    component.form.patchValue({
      label: '  Promotion ',
      amount: 500,
      currency: 'EUR',
      when: {from: '2027-03-01', to: '2027-04-01'},
      note: '   ',
    });

    component.submit();

    expect(close).toHaveBeenCalledWith({
      kind: 'IncomeStep',
      date: '2027-03-01',
      endDate: null,
      label: 'Promotion',
      amount: 500,
      currency: 'EUR',
      note: null,
    });
  });

  it('keeps an edited event on its kind', () => {
    const {component, close} = setup(FUTURE_RAISE);
    component.form.patchValue({amount: 650});

    component.submit();

    expect(close).toHaveBeenCalledWith(
      expect.objectContaining({kind: 'IncomeStep', amount: 650, date: '2027-03-01', endDate: null})
    );
  });

  it('closes without a result on cancel', () => {
    const {component, close} = setup();

    component.cancel();

    expect(close).toHaveBeenCalledWith();
  });
});
