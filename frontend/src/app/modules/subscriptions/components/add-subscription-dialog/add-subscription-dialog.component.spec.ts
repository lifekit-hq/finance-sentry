import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it, vi} from 'vitest';

import {AddSubscriptionDialogComponent} from './add-subscription-dialog.component';

function setup() {
  const close = vi.fn();
  TestBed.configureTestingModule({providers: [{provide: DialogRef, useValue: {close}}]});
  return {
    component: TestBed.createComponent(AddSubscriptionDialogComponent).componentInstance,
    close,
  };
}

describe('AddSubscriptionDialogComponent', () => {
  it('does not close while the form is invalid', () => {
    const {component, close} = setup();
    component.submit();
    expect(close).not.toHaveBeenCalled();
    expect(component.form.touched).toBe(true);
  });

  it('closes with the request when valid', () => {
    const {component, close} = setup();
    component.form.patchValue({
      merchant: 'Gym',
      monthlyAmount: 25,
      currency: 'EUR',
      startDate: '2026-01-15',
    });
    component.submit();
    expect(close).toHaveBeenCalledWith({
      merchant: 'Gym',
      monthlyAmount: 25,
      currency: 'EUR',
      startDate: '2026-01-15',
    });
  });

  it('closes without a value on cancel', () => {
    const {component, close} = setup();
    component.cancel();
    expect(close).toHaveBeenCalledWith();
  });
});
