import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {CmnDialogService} from '@lifekit-hq/ui';
import {of} from 'rxjs';
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
    create: vi.fn(),
    remove: vi.fn(),
    navigateToPeriod: vi.fn(),
  };
  const dialogResult = signal<unknown>(undefined);
  const dialog = {open: vi.fn(() => ({afterClosed: () => of(dialogResult())}))};
  const router = {navigate: vi.fn()};
  TestBed.configureTestingModule({
    providers: [
      {provide: BudgetsStore, useValue: store},
      {provide: Router, useValue: router},
      {provide: CmnDialogService, useValue: dialog},
      {provide: CategoryStore, useValue: {categories: signal([]), colorMap: signal({})}},
    ],
  });
  TestBed.overrideComponent(BudgetsComponent, {
    set: {providers: [{provide: BudgetsStore, useValue: store}], template: ''},
  });
  const component = TestBed.createComponent(BudgetsComponent).componentInstance;
  return {component, store, dialog, dialogResult, router};
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

describe('BudgetsComponent actions', () => {
  it('creates a budget from the add dialog result', () => {
    const {component, store, dialogResult} = setup();
    dialogResult.set({category: 'groceries', monthlyLimit: 300});
    component.openAddBudget();
    expect(store.create).toHaveBeenCalledWith({category: 'groceries', monthlyLimit: 300});
  });

  it('creates nothing when the add dialog is dismissed', () => {
    const {component, store} = setup();
    component.openAddBudget();
    expect(store.create).not.toHaveBeenCalled();
  });

  it('routes menu actions to edit, transactions and remove', () => {
    const {component, store, router} = setup();
    const budget = {id: 'b1', category: 'groceries', monthlyLimit: 100};
    component.onBudgetAction('edit', budget);
    expect(store.setEditing).toHaveBeenCalledWith('b1');
    component.onBudgetAction('transactions', budget);
    expect(router.navigate).toHaveBeenCalled();
    component.onBudgetAction('remove', budget);
    expect(store.remove).toHaveBeenCalledWith('b1');
  });

  it('navigates the store to the stepped month', () => {
    const {component, store} = setup();
    component.onMonthChange(new Date(2026, 8, 1));
    expect(store.navigateToPeriod).toHaveBeenCalledWith({year: 2026, month: 9});
  });

  it('colours the bar red over limit, amber near it, category colour otherwise', () => {
    const {component} = setup();
    expect(component.barColor(120, 100, 'x')).toContain('error');
    expect(component.barColor(85, 100, 'x')).toContain('warning');
    expect(component.barColor(10, 100, 'x')).toBe('#94a3b8');
  });
});
