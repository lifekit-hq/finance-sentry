import {TestBed} from '@angular/core/testing';
import {CMN_DRAWER_DATA, CmnDrawerRef} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {type AlertFilter} from '../../models/alert/alert.model';
import {AlertFilterSheetComponent} from './alert-filter-sheet.component';

function setup(initial: AlertFilter) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      {provide: CMN_DRAWER_DATA, useValue: initial},
      {provide: CmnDrawerRef, useValue: {close}},
    ],
  });
  const fixture = TestBed.createComponent(AlertFilterSheetComponent);
  fixture.detectChanges();
  return {component: fixture.componentInstance, close};
}

describe('AlertFilterSheetComponent', () => {
  it('starts from the page filter and hands the draft back on apply', () => {
    const {component, close} = setup('unread');

    expect(component.draft()).toBe('unread');
    component.select('error');
    component.apply();

    expect(close).toHaveBeenCalledWith('error');
  });

  it('resets the draft to all', () => {
    const {component} = setup('warning');

    component.reset();

    expect(component.draft()).toBe('all');
  });
});
