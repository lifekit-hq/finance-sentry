import {TestBed} from '@angular/core/testing';
import {describe, expect, it} from 'vitest';

import {type AlertFilter} from '../../models/alert/alert.model';
import {AlertFilterSheetComponent} from './alert-filter-sheet.component';

function setup(initial: AlertFilter) {
  const fixture = TestBed.createComponent(AlertFilterSheetComponent);
  fixture.componentRef.setInput('initial', initial);
  fixture.detectChanges();
  const applied: AlertFilter[] = [];
  fixture.componentInstance.applied.subscribe(f => applied.push(f));
  return {component: fixture.componentInstance, applied};
}

describe('AlertFilterSheetComponent', () => {
  it('starts from the page filter and emits the draft on apply', () => {
    const {component, applied} = setup('unread');

    expect(component.draft()).toBe('unread');
    component.select('error');
    component.apply();

    expect(applied).toEqual(['error']);
  });

  it('resets the draft to all without emitting', () => {
    const {component, applied} = setup('warning');

    component.reset();

    expect(component.draft()).toBe('all');
    expect(applied).toHaveLength(0);
  });
});
