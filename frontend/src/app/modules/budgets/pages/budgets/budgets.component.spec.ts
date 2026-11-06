import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {type AsyncStatus} from '@lifekit-hq/core';
import {ASYNC_STATE_SHOW_DELAY_MS} from '@lifekit-hq/ui';
import {describe, expect, it, onTestFinished, vi} from 'vitest';

import {type BudgetSummaryItem} from '../../models/budget/budget.model';
import {BudgetsStore} from '../../store/budgets/budgets.store';
import {BudgetsComponent} from './budgets.component';

function item(overrides: Partial<BudgetSummaryItem> = {}): BudgetSummaryItem {
  return {
    overrideId: null,
    category: 'FOOD_AND_DRINK',
    categoryLabel: 'Food & drink',
    estimate: 150,
    suggestedAmount: 150,
    source: 'history',
    spent: 100,
    remaining: 50,
    isOverBudget: false,
    currency: 'USD',
    paceRatio: 1,
    projectedMonthEndSpend: 100,
    isOffPace: false,
    expectedMonthEnd: 150,
    ...overrides,
  };
}

function setup() {
  const editingCategory = signal<string | null>(null);
  const store = {
    editingCategory,
    setEditing: vi.fn((category: string | null) => editingCategory.set(category)),
    saveOverride: vi.fn(),
    resetOverride: vi.fn(),
    navigateToPeriod: vi.fn(),
    selectedYear: signal(2026),
    selectedMonth: signal(7),
  };
  const router = {navigate: vi.fn()};
  TestBed.configureTestingModule({
    providers: [
      {provide: BudgetsStore, useValue: store},
      {provide: Router, useValue: router},
    ],
  });
  TestBed.overrideComponent(BudgetsComponent, {
    set: {providers: [{provide: BudgetsStore, useValue: store}], template: ''},
  });
  const component = TestBed.createComponent(BudgetsComponent).componentInstance;
  return {component, store, router};
}

describe('BudgetsComponent inline estimate edit', () => {
  it('saves the typed amount as an override on Enter or blur', () => {
    const {component, store} = setup();
    const line = item();
    component.startEdit(line.category, line.estimate);
    component.editValue.set('250');
    component.saveEdit(line);
    expect(store.saveOverride).toHaveBeenCalledWith({
      category: 'FOOD_AND_DRINK',
      overrideId: null,
      amount: 250,
    });
    expect(store.editingCategory()).toBeNull();
  });

  it('updates the override a line already has', () => {
    const {component, store} = setup();
    const line = item({overrideId: 'o1', source: 'override'});
    component.startEdit(line.category, line.estimate);
    component.editValue.set('300');
    component.saveEdit(line);
    expect(store.saveOverride).toHaveBeenCalledWith({
      category: 'FOOD_AND_DRINK',
      overrideId: 'o1',
      amount: 300,
    });
  });

  it('starts empty on a line with no estimate yet', () => {
    const {component} = setup();
    component.startEdit('MEDICAL', null);
    expect(component.editValue()).toBe('');
  });

  it('does not save a blank or non-positive amount', () => {
    const {component, store} = setup();
    const line = item();
    component.startEdit(line.category, null);
    component.saveEdit(line);
    component.startEdit(line.category, null);
    component.editValue.set('0');
    component.saveEdit(line);
    expect(store.saveOverride).not.toHaveBeenCalled();
  });

  it('does not save when Escape cancelled the edit before the late blur', () => {
    const {component, store} = setup();
    const line = item();
    component.startEdit(line.category, line.estimate);
    component.editValue.set('999');
    component.cancelEdit();
    component.saveEdit(line);
    expect(store.saveOverride).not.toHaveBeenCalled();
  });

  it('does not save twice when blur follows Enter', () => {
    const {component, store} = setup();
    const line = item();
    component.startEdit(line.category, line.estimate);
    component.editValue.set('250');
    component.saveEdit(line);
    component.saveEdit(line);
    expect(store.saveOverride).toHaveBeenCalledTimes(1);
  });
});

describe('BudgetsComponent actions', () => {
  it('routes menu actions to edit, transactions and reset', () => {
    const {component, store, router} = setup();
    const line = item({overrideId: 'o1', source: 'override'});
    component.onBudgetAction('edit', line);
    expect(store.setEditing).toHaveBeenCalledWith('FOOD_AND_DRINK');
    component.onBudgetAction('transactions', line);
    expect(router.navigate).toHaveBeenCalled();
    component.onBudgetAction('reset', line);
    expect(store.resetOverride).toHaveBeenCalledWith('o1');
  });

  it('has nothing to reset on a line that runs on its suggestion', () => {
    const {component, store} = setup();
    component.onBudgetAction('reset', item());
    expect(store.resetOverride).not.toHaveBeenCalled();
  });

  it('offers a reset only where a hand edit exists, and a removal where it has no suggestion', () => {
    const {component} = setup();
    expect(component.menuFor(item()).map(m => m.id)).toEqual(['edit', 'transactions']);
    const reset = component.menuFor(item({overrideId: 'o1', source: 'override'}));
    expect(reset.map(m => m.label)).toContain('Reset to suggested');
    const remove = component.menuFor(
      item({overrideId: 'o1', source: 'override', suggestedAmount: null})
    );
    expect(remove.map(m => m.label)).toContain('Remove my amount');
  });

  it('offers no transactions view on a counterparty-role line', () => {
    const {component} = setup();
    for (const category of ['HOUSEHOLD', 'FAMILY_SUPPORT']) {
      expect(component.menuFor(item({category})).map(m => m.id)).toEqual(['edit']);
      const reset = component.menuFor(item({category, overrideId: 'o1', source: 'override'}));
      expect(reset.map(m => m.id)).toEqual(['edit', 'reset']);
      const remove = component.menuFor(
        item({category, overrideId: 'o1', source: 'override', suggestedAmount: null})
      );
      expect(remove.map(m => m.label)).toEqual(['Edit estimate', 'Remove my amount']);
    }
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

  it('colours the bar red over the estimate, amber near it, category colour otherwise', () => {
    const {component} = setup();
    expect(component.barColor(120, 100, 'FOOD_AND_DRINK')).toContain('error');
    expect(component.barColor(95, 100, 'FOOD_AND_DRINK')).toContain('warning');
    expect(component.barColor(85, 100, 'FOOD_AND_DRINK')).toBe('var(--color-chart-series-2)');
  });
});

interface RenderOptions {
  status?: AsyncStatus;
  items?: BudgetSummaryItem[];
  refreshFailed?: boolean;
  totalEstimate?: number;
  totalSpent?: number;
  expectedMonthEnd?: number;
  hasEnoughHistory?: boolean;
  historyMonthsUsed?: number;
  skippedNote?: string | null;
  spendIncomplete?: boolean;
  isCurrentMonth?: boolean;
}

function render(options: RenderOptions = {}) {
  const items = options.items ?? [];
  const hasEnoughHistory = options.hasEnoughHistory ?? true;
  const store = {
    editingCategory: signal<string | null>(null),
    monthName: signal('September'),
    overBudgetCount: signal(items.filter(i => i.isOverBudget).length),
    overallPct: signal(0),
    selectedMonthDate: signal(new Date(2026, 8, 1)),
    selectedYear: signal(2026),
    selectedMonth: signal(9),
    status: signal<AsyncStatus>(options.status ?? 'idle'),
    refreshFailed: signal(options.refreshFailed ?? false),
    loadSummary: vi.fn(),
    items: signal(items),
    totalEstimate: signal(options.totalEstimate ?? 150),
    totalSpent: signal(options.totalSpent ?? 100),
    expectedMonthEnd: signal(options.expectedMonthEnd ?? 150),
    hasEnoughHistory: signal(hasEnoughHistory),
    historyMonthsUsed: signal(options.historyMonthsUsed ?? 6),
    minHistoryMonths: signal(3),
    skippedNote: signal(options.skippedNote ?? null),
    spendIncomplete: signal(options.spendIncomplete ?? false),
    isCurrentMonth: signal(options.isCurrentMonth ?? true),
  };
  TestBed.configureTestingModule({providers: [provideRouter([])]});
  TestBed.overrideComponent(BudgetsComponent, {
    set: {providers: [{provide: BudgetsStore, useValue: store}]},
  });
  const fixture = TestBed.createComponent(BudgetsComponent);
  fixture.detectChanges();
  const host = fixture.nativeElement as HTMLElement;
  const text = host.textContent?.replace(/\s+/g, ' ') ?? '';
  return {fixture, host, store, text};
}

describe('BudgetsComponent estimate copy', () => {
  it('reads "<amount> left of <estimate>" under the estimate', () => {
    expect(render({items: [item()]}).text).toContain('$50.00 left of $150.00');
  });

  it('reads "<amount> over · <estimate> estimate" past it', () => {
    const text = render({
      items: [item({spent: 171.2, remaining: -21.2, isOverBudget: true})],
    }).text;
    expect(text).toContain('$21.20 over · $150.00 estimate');
    expect(text).not.toContain('over of');
  });

  it('headlines where the month is expected to end', () => {
    const {text, host} = render({items: [item()], expectedMonthEnd: 1430, totalEstimate: 1100});
    expect(text).toContain('Expected by month end');
    expect(host.querySelector('[data-testid="budgets-expected"]')?.textContent?.trim()).toBe(
      '$1,430.00'
    );
    expect(text).toContain('of $1,100.00 estimated');
  });

  it('calls a past month what was spent in it', () => {
    expect(render({items: [item()], isCurrentMonth: false}).text).toContain('Spent in September');
  });

  it('says where an off-pace line is heading', () => {
    const text = render({
      items: [item({isOffPace: true, paceRatio: 1.71, projectedMonthEndSpend: 171})],
    }).text;
    expect(text).toContain('Off pace');
    expect(text).toContain('On track to finish the month at 171% of the estimate');
  });

  it('says nothing about pace when the line is on pace', () => {
    expect(render({items: [item()]}).text).not.toContain('On track to finish');
  });

  it('says where each estimate comes from', () => {
    const text = render({
      items: [
        item(),
        item({category: 'LOAN_PAYMENTS', categoryLabel: 'Loans', source: 'commitment'}),
        item({
          category: 'TRAVEL',
          categoryLabel: 'Travel',
          source: 'override',
          overrideId: 'o1',
          estimate: 600,
          suggestedAmount: 200,
        }),
      ],
    }).text;
    expect(text).toContain('A typical month for you');
    expect(text).toContain('Fixed monthly charges');
    expect(text).toContain('Your amount · suggested $200.00');
  });

  it('shows spend with no estimate as spend, not as a zero budget', () => {
    const text = render({
      items: [
        item({
          category: 'MEDICAL',
          estimate: null,
          suggestedAmount: null,
          source: 'none',
          spent: 75,
          remaining: 0,
        }),
      ],
    }).text;
    expect(text).toContain('$75.00 spent · no estimate yet');
    expect(text).not.toContain('left of');
  });
});

describe('BudgetsComponent months left out', () => {
  const NOTE = 'September 2026 and October 2026 are left out of these estimates.';

  it('says in plain words which months the estimates do not learn from', () => {
    const {host} = render({items: [item()], skippedNote: NOTE});
    expect(host.querySelector('[data-testid="budgets-skipped-note"]')?.textContent).toContain(NOTE);
  });

  it('shows no note when no month is left out', () => {
    expect(
      render({items: [item()]}).host.querySelector('[data-testid="budgets-skipped-note"]')
    ).toBeNull();
  });
});

describe('BudgetsComponent month with a bank-link gap', () => {
  it('says the month is partly unseen instead of letting a thin figure read as thrift', () => {
    const {host} = render({items: [item({spent: 12})], spendIncomplete: true});
    expect(host.querySelector('[data-testid="budgets-spend-incomplete"]')?.textContent).toContain(
      'only what reached the app'
    );
  });

  it('shows no such note when the month is whole', () => {
    expect(
      render({items: [item()]}).host.querySelector('[data-testid="budgets-spend-incomplete"]')
    ).toBeNull();
  });
});

describe('BudgetsComponent async states', () => {
  it('shows a skeleton while the month loads, never the empty copy', () => {
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    const {fixture, host} = render({status: 'loading'});
    vi.advanceTimersByTime(ASYNC_STATE_SHOW_DELAY_MS);
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="budgets-skeleton"]')).not.toBeNull();
    expect(host.textContent).not.toContain('Nothing to estimate');
  });

  it('shows the load error with a Retry that reloads the month', () => {
    const {host, store, fixture} = render({status: 'error'});

    expect(host.textContent).toContain('Failed to load the monthly estimates.');
    expect(host.textContent).not.toContain('Nothing to estimate');

    Array.from(host.querySelectorAll('button'))
      .find(b => b.textContent?.trim() === 'Retry')
      ?.click();
    fixture.detectChanges();

    expect(store.loadSummary).toHaveBeenCalledTimes(1);
  });

  it('replaces the cards with the error when the month fails to load', () => {
    const {host} = render({status: 'error', items: [item()]});

    expect(host.textContent).toContain('Failed to load the monthly estimates.');
    expect(host.textContent).not.toContain('Food & drink');
  });

  it('keeps the valid cards under the error when the refresh after an edit fails', () => {
    const {host} = render({status: 'success', items: [item()], refreshFailed: true});

    expect(host.textContent).toContain('Failed to load the monthly estimates.');
    expect(host.textContent).toContain('Food & drink');
    expect(host.querySelector('[data-testid="budgets-skeleton"]')).toBeNull();
  });

  it('says when there is not enough history yet, and how much there is', () => {
    const {text} = render({hasEnoughHistory: false, historyMonthsUsed: 2});

    expect(text).toContain('Not enough history to estimate yet');
    expect(text).toContain('at least 3 months of bank history; 2 so far');
  });

  it('shows a quiet month as nothing to estimate, once there is enough history', () => {
    expect(render().text).toContain('Nothing to estimate for September');
  });

  it('still shows spend lines, with a short-history note, when history is too short', () => {
    const {host, text} = render({
      hasEnoughHistory: false,
      historyMonthsUsed: 2,
      items: [
        item({
          category: 'MEDICAL',
          categoryLabel: 'Medical',
          estimate: null,
          suggestedAmount: null,
          source: 'none',
        }),
      ],
    });
    expect(host.querySelector('[data-testid="budgets-short-history"]')).not.toBeNull();
    expect(text).toContain('Medical');
  });

  it('links each category to its debit transactions within the month', () => {
    const {host} = render({items: [item({category: 'groceries'})]});

    expect(host.querySelector('a[data-testid="budget-category-link"]')?.getAttribute('href')).toBe(
      '/transactions?category=groceries&type=debit&from=2026-09-01&to=2026-09-30'
    );
  });

  it('shows a counterparty-role line as plain text, not a link to a filter that matches nothing', () => {
    const {host} = render({
      items: [item({category: 'HOUSEHOLD', categoryLabel: 'Household'})],
    });

    expect(host.querySelector('a[data-testid="budget-category-link"]')).toBeNull();
    expect(host.querySelector('[data-testid="budget-category-label"]')?.textContent).toContain(
      'Household'
    );
  });
});
