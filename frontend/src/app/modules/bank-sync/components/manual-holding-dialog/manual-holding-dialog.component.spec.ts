import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type ManualHolding} from '../../models/manual-holding/manual-holding.model';
import {ManualHoldingDialogComponent} from './manual-holding-dialog.component';

const steam: ManualHolding = {
  id: 'manual-1',
  label: 'Steam wallet',
  amount: 40,
  currency: 'EUR',
  asOf: '2026-10-01',
  convertibility: 'Locked',
  reliability: 'L3Estimated',
  updatedAt: '2026-10-01T10:00:00Z',
};

function setup(holding: ManualHolding | null = null) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: DialogRef, useValue: {close}},
      {provide: CMN_DIALOG_DATA, useValue: {holding}},
    ],
  });
  return {
    component: TestBed.createComponent(ManualHoldingDialogComponent).componentInstance,
    close,
  };
}

describe('ManualHoldingDialogComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-19T12:00:00'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts a new entry in dollars, spendable only inside its platform', () => {
    const {component} = setup();

    expect(component.isEdit).toBe(false);
    expect(component.form.getRawValue()).toEqual({
      label: '',
      amount: null,
      currency: 'USD',
      convertibility: 'InEconomy',
    });
  });

  it('opens an edit with the entry filled in', () => {
    const {component} = setup(steam);

    expect(component.isEdit).toBe(true);
    expect(component.form.getRawValue()).toEqual({
      label: 'Steam wallet',
      amount: 40,
      currency: 'EUR',
      convertibility: 'Locked',
    });
  });

  it('does not close while a field is missing or the value is not positive', () => {
    const {component, close} = setup();

    component.submit();
    component.form.patchValue({label: 'Steam wallet', amount: 0});
    component.submit();

    expect(close).not.toHaveBeenCalled();
  });

  it('closes with the trimmed entry, as of today', () => {
    const {component, close} = setup();
    component.form.patchValue({label: '  Steam wallet ', amount: 42.5});

    component.submit();

    expect(close).toHaveBeenCalledWith({
      label: 'Steam wallet',
      amount: 42.5,
      currency: 'USD',
      asOf: '2026-10-19',
      convertibility: 'InEconomy',
    });
  });

  it('closes without a value on cancel', () => {
    const {component, close} = setup();

    component.cancel();

    expect(close).toHaveBeenCalledWith();
  });
});
