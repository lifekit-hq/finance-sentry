import {DialogRef} from '@angular/cdk/dialog';
import {TestBed} from '@angular/core/testing';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {SetTermDialogComponent} from './set-term-dialog.component';

function setup(termCount: number | null) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: DialogRef, useValue: {close}},
      {provide: CMN_DIALOG_DATA, useValue: {termCount}},
    ],
  });
  const component = TestBed.createComponent(SetTermDialogComponent).componentInstance;
  return {component, close};
}

describe('SetTermDialogComponent', () => {
  it('starts from the current term', () => {
    expect(setup(12).component.control.value).toBe(12);
  });

  it('closes with the entered term', () => {
    const {component, close} = setup(null);
    component.control.setValue(24);
    component.save();
    expect(close).toHaveBeenCalledWith(24);
  });

  it('closes with null when the term is cleared', () => {
    const {component, close} = setup(12);
    component.control.setValue(null);
    component.save();
    expect(close).toHaveBeenCalledWith(null);
  });

  it('closes without a value on cancel', () => {
    const {component, close} = setup(12);
    component.cancel();
    expect(close).toHaveBeenCalledWith();
  });
});
