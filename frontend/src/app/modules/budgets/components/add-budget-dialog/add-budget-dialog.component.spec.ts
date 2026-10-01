import {DialogRef} from '@angular/cdk/dialog';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it, vi} from 'vitest';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {AddBudgetDialogComponent} from './add-budget-dialog.component';

function setup() {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: DialogRef, useValue: {close}},
      {
        provide: CategoryStore,
        useValue: {categories: signal([{key: 'groceries', label: 'Groceries', sortOrder: 1}])},
      },
    ],
  });
  return {component: TestBed.createComponent(AddBudgetDialogComponent).componentInstance, close};
}

describe('AddBudgetDialogComponent', () => {
  it('offers the known categories', () => {
    expect(setup().component.categoryOptions()).toEqual([{value: 'groceries', label: 'Groceries'}]);
  });

  it('does not close while the form is invalid', () => {
    const {component, close} = setup();
    component.submit();
    expect(close).not.toHaveBeenCalled();
  });

  it('closes with the request when valid', () => {
    const {component, close} = setup();
    component.form.patchValue({category: 'groceries', monthlyLimit: 300});
    component.submit();
    expect(close).toHaveBeenCalledWith({category: 'groceries', monthlyLimit: 300});
  });

  it('closes without a value on cancel', () => {
    const {component, close} = setup();
    component.cancel();
    expect(close).toHaveBeenCalledWith();
  });
});
