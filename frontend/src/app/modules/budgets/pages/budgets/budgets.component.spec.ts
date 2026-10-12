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
    selectedYear: signal(2026),
    selectedMonth: signal(7),
  };
  const dialogResult = signal<unknown>(undefined);
  const dialog = {open: vi.fn(() => ({afterClosed: () => of(dialogResult())}))};
  const router = {navigate: vi.fn()};
  TestBed.configureTestingModule({
    providers: [
      {provide: BudgetsStore, useValue: store},
      {provide: Router, useValue: router},
      {provide: CmnDialogService, useValue: dialog},
      {provide: CategoryStore, useValue: {categories: signal([])}},
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

  it('opens the debit transactions of the category within the selected month', () => {
    const {component, router} = setup();
    component.viewTransactions('groceries');
    expect(router.navigate).toHaveBeenCalledWith(['/transactions'], {
      queryParams: {category: 'groceries', type: 'debit', from: '2026-07-01', to: '2026-07-31'},
    });
  });

  it('navigates the store to the stepped month', () => {
    const {component, store} = setup();
    component.onMonthChange(new Date(2026, 8, 1));
    expect(store.navigateToPeriod).toHaveBeenCalledWith({year: 2026, month: 9});
  });

  it('colours the bar red over limit, amber near it, category colour otherwise', () => {
    const {component} = setup();
    expect(component.barColor(120, 100, 0)).toContain('error');
    expect(component.barColor(85, 100, 0)).toContain('warning');
    expect(component.barColor(10, 100, 1)).toBe('var(--color-chart-series-2)');
  });
});

describe('BudgetsComponent hero copy', () => {
  function render(spent: number, budget: number, isOffPace = false): string {
    const store = {
      editingId: signal<string | null>(null),
      monthName: signal('September'),
      overBudgetCount: signal(0),
      overallPct: signal(0),
      selectedMonthDate: signal(new Date(2026, 8, 1)),
      status: signal<AsyncStatus>('idle'),
      refreshFailed: signal(false),
      loadSummary: vi.fn(),
      summaryItems: signal([
        {
          id: 'b1',
          category: 'groceries',
          categoryLabel: 'Groceries',
          monthlyLimit: budget,
          spent,
          remaining: budget - spent,
          isOverBudget: spent > budget,
          currency: 'USD',
          paceRatio: 1.71,
          projectedMonthEndSpend: spent * 1.71,
          isOffPace,
        },
      ]),
      totalBudget: signal(budget),
      totalSpent: signal(spent),
    };
    TestBed.configureTestingModule({
      providers: [
        {provide: Router, useValue: {navigate: vi.fn()}},
        {provide: CmnDialogService, useValue: {open: vi.fn()}},
        {provide: CategoryStore, useValue: {categories: signal([])}},
      ],
    });
    TestBed.overrideComponent(BudgetsComponent, {
      set: {providers: [{provide: BudgetsStore, useValue: store}]},
    });
    const fixture = TestBed.createComponent(BudgetsComponent);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  it('reads "<amount> over · <limit> budget" when over budget', () => {
    const text = render(171.2, 150);
    expect(text).toContain('$21.20 over · $150.00 budget');
    expect(text).not.toContain('over of');
  });

  it('reads "<amount> left of <limit>" when under budget', () => {
    expect(render(100, 150)).toContain('$50.00 left of $150.00');
  });

  it('says where an off-pace budget is heading', () => {
    const text = render(100, 150, true);
    expect(text).toContain('Off pace');
    expect(text).toContain('On track to finish the month at 171% of the limit');
  });

  it('says nothing about pace when the budget is on pace', () => {
    expect(render(100, 150)).not.toContain('On track to finish');
  });
});

describe('BudgetsComponent async states', () => {
  function render(status: AsyncStatus, withItems = false, refreshFailed = false) {
    const store = {
      editingId: signal<string | null>(null),
      monthName: signal('September'),
      overBudgetCount: signal(0),
      overallPct: signal(0),
      selectedMonthDate: signal(new Date(2026, 8, 1)),
      selectedYear: signal(2026),
      selectedMonth: signal(9),
      status: signal<AsyncStatus>(status),
      refreshFailed: signal(refreshFailed),
      loadSummary: vi.fn(),
      summaryItems: signal(
        withItems
          ? [
              {
                id: 'b1',
                category: 'groceries',
                categoryLabel: 'Groceries',
                monthlyLimit: 150,
                spent: 100,
                remaining: 50,
                isOverBudget: false,
                currency: 'USD',
                paceRatio: 1,
                projectedMonthEndSpend: 100,
                isOffPace: false,
              },
            ]
          : []
      ),
      totalBudget: signal(150),
      totalSpent: signal(100),
    };
    TestBed.configureTestingModule({
      providers: [
        {provide: Router, useValue: {navigate: vi.fn()}},
        {provide: CmnDialogService, useValue: {open: vi.fn()}},
        {provide: CategoryStore, useValue: {categories: signal([])}},
      ],
    });
    TestBed.overrideComponent(BudgetsComponent, {
      set: {providers: [{provide: BudgetsStore, useValue: store}]},
    });
    const fixture = TestBed.createComponent(BudgetsComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    return {fixture, host, store};
  }

  it('shows a skeleton while the month loads, never the empty copy', () => {
    const {host} = render('loading');

    expect(host.querySelector('[data-testid="budgets-skeleton"]')).not.toBeNull();
    expect(host.textContent).not.toContain('No budgets for September');
  });

  it('shows the load error with a Retry that reloads the month', () => {
    const {host, store, fixture} = render('error');

    expect(host.textContent).toContain('Failed to load budgets.');
    expect(host.textContent).not.toContain('No budgets for September');

    Array.from(host.querySelectorAll('button'))
      .find(b => b.textContent?.trim() === 'Retry')
      ?.click();
    fixture.detectChanges();

    expect(store.loadSummary).toHaveBeenCalledTimes(1);
  });

  it('replaces the cards with the error when the month fails to load', () => {
    const {host} = render('error', true);

    expect(host.textContent).toContain('Failed to load budgets.');
    expect(host.textContent).not.toContain('Groceries');
  });

  it('keeps the valid cards under the error when the refresh after an edit fails', () => {
    const {host} = render('idle', true, true);

    expect(host.textContent).toContain('Failed to load budgets.');
    expect(host.textContent).toContain('Groceries');
    expect(host.querySelector('[data-testid="budgets-skeleton"]')).toBeNull();
  });

  it('shows the empty state with its prompt when the month has no budgets', () => {
    const {host} = render('idle');

    expect(host.textContent).toContain('No budgets for September');
    expect(host.textContent).toContain(
      'Set a monthly limit for a category to see how much you have left.'
    );
  });

  it('shows the category cards once loaded', () => {
    const {host} = render('idle', true);

    expect(host.textContent).toContain('Groceries');
    expect(host.querySelector('[data-testid="budgets-skeleton"]')).toBeNull();
    expect(host.textContent).not.toContain('No budgets for September');
  });
});
