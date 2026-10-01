import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {describe, expect, it, vi} from 'vitest';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {BudgetsStore} from '../../store/budgets/budgets.store';
import {BudgetsComponent} from './budgets.component';

function setup() {
  const editingId = signal<string | null>(null);
  const store = {
    editingId,
    setEditing: vi.fn((id: string | null) => editingId.set(id)),
    update: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      {provide: BudgetsStore, useValue: store},
      {provide: Router, useValue: {}},
      {provide: CategoryStore, useValue: {categories: signal([])}},
    ],
  });
  const component = TestBed.runInInjectionContext(() => new BudgetsComponent());
  return {component, store};
}

describe('BudgetsComponent inline limit edit', () => {
  it('saves the typed limit on Enter or blur', () => {
    const {component, store} = setup();
    component.startEdit('b1', 100);
    component.editValue.set('250');
    component.saveEdit('b1');
    expect(store.update).toHaveBeenCalledWith({id: 'b1', monthlyLimit: 250});
    expect(store.editingId()).toBeNull();
  });

  it('does not save when Escape cancelled the edit before the late blur', () => {
    const {component, store} = setup();
    component.startEdit('b1', 100);
    component.editValue.set('999');
    component.cancelEdit();
    component.saveEdit('b1');
    expect(store.update).not.toHaveBeenCalled();
  });

  it('does not save twice when blur follows Enter', () => {
    const {component, store} = setup();
    component.startEdit('b1', 100);
    component.editValue.set('250');
    component.saveEdit('b1');
    component.saveEdit('b1');
    expect(store.update).toHaveBeenCalledTimes(1);
  });
});
