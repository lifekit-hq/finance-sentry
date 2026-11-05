import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type IncomeSource} from '../../models/income-source/income-source.model';
import {IncomeSourceDialogComponent} from './income-source-dialog.component';

const SALARY: IncomeSource = {
  id: 's-1',
  kind: 'Side',
  label: 'Weekend gig',
  amount: 250,
  currency: 'EUR',
  cadence: 'Weekly',
  grossOrNet: 'Gross',
  startDate: '2026-02-01',
  endDate: '2026-09-30',
  monthlyEquivalent: 1083.33,
  active: false,
  updatedAt: '2026-10-01T10:00:00Z',
};

function setup(source: IncomeSource | null = null) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: DialogRef, useValue: {close}},
      {provide: CMN_DIALOG_DATA, useValue: {source}},
    ],
  });
  return {
    component: TestBed.createComponent(IncomeSourceDialogComponent).componentInstance,
    close,
  };
}

describe('IncomeSourceDialogComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-19T12:00:00'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts a new source as a monthly net salary in dollars', () => {
    const {component} = setup();

    expect(component.isEdit).toBe(false);
    expect(component.form.getRawValue()).toEqual({
      label: '',
      kind: 'Salary',
      amount: null,
      currency: 'USD',
      cadence: 'Monthly',
      grossOrNet: 'Net',
    });
  });

  it('opens an edit with the source filled in', () => {
    const {component} = setup(SALARY);

    expect(component.isEdit).toBe(true);
    expect(component.form.getRawValue()).toEqual({
      label: 'Weekend gig',
      kind: 'Side',
      amount: 250,
      currency: 'EUR',
      cadence: 'Weekly',
      grossOrNet: 'Gross',
    });
  });

  it('refuses an empty form and closes nothing', () => {
    const {component, close} = setup();

    component.submit();

    expect(close).not.toHaveBeenCalled();
    expect(component.form.controls.label.touched).toBe(true);
  });

  it('refuses a zero amount', () => {
    const {component, close} = setup();
    component.form.patchValue({label: 'Job', amount: 0});

    component.submit();

    expect(close).not.toHaveBeenCalled();
  });

  it('starts a new source today with no end date', () => {
    const {component, close} = setup();
    component.form.patchValue({label: '  Day job ', amount: 1000});

    component.submit();

    expect(close).toHaveBeenCalledWith({
      kind: 'Salary',
      label: 'Day job',
      amount: 1000,
      currency: 'USD',
      cadence: 'Monthly',
      grossOrNet: 'Net',
      startDate: '2026-10-19',
      endDate: null,
    });
  });

  it('keeps the dates of an edited source', () => {
    const {component, close} = setup(SALARY);
    component.form.patchValue({amount: 300});

    component.submit();

    expect(close).toHaveBeenCalledWith(
      expect.objectContaining({amount: 300, startDate: '2026-02-01', endDate: '2026-09-30'})
    );
  });

  it('closes without a result on cancel', () => {
    const {component, close} = setup();

    component.cancel();

    expect(close).toHaveBeenCalledWith();
  });
});
