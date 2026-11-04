import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type BudgetSummaryResponse} from '../../models/budget/budget.model';
import {BudgetsService} from '../../services/budgets.service';
import {budgetsEffects} from './budgets.effects';

const SUMMARY: BudgetSummaryResponse = {
  year: 2027,
  month: 4,
  items: [],
  totalEstimate: 0,
  totalSpent: 0,
  expectedMonthEnd: 0,
  hasEnoughHistory: true,
  historyMonthsUsed: 6,
  minHistoryMonths: 3,
  skippedMonths: [],
};
const FAILURE = () => throwError(() => new HttpErrorResponse({status: 500}));

function buildStore() {
  return {
    setSummary: vi.fn(),
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    clearRefreshFailed: vi.fn(),
    setError: vi.fn(),
    setRefreshFailed: vi.fn(),
    setEditing: vi.fn(),
    setSelectedPeriod: vi.fn(),
    selectedYear: () => 2027,
    selectedMonth: () => 4,
  };
}

function buildService() {
  return {
    getBudgetSummary: vi.fn(),
    createBudget: vi.fn(),
    updateBudget: vi.fn(),
    deleteBudget: vi.fn(),
  };
}

function effectsFor(
  service: ReturnType<typeof buildService>,
  store: ReturnType<typeof buildStore>
) {
  TestBed.configureTestingModule({providers: [{provide: BudgetsService, useValue: service}]});
  return TestBed.runInInjectionContext(() => budgetsEffects(store));
}

describe('budgetsEffects', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loadSummary: shows loading then stores the month', () => {
    const store = buildStore();
    const service = buildService();
    service.getBudgetSummary.mockReturnValue(of(SUMMARY));

    effectsFor(service, store).loadSummary();

    expect(store.setLoading).toHaveBeenCalledTimes(1);
    expect(store.setSummary).toHaveBeenCalledWith(SUMMARY);
    expect(store.setSuccess).toHaveBeenCalledTimes(1);
    expect(store.clearRefreshFailed).toHaveBeenCalledTimes(1);
    expect(store.setError).not.toHaveBeenCalled();
  });

  it('loadSummary: replaces the page with the error when the month fails', () => {
    const store = buildStore();
    const service = buildService();
    service.getBudgetSummary.mockReturnValue(FAILURE());

    effectsFor(service, store).loadSummary();

    expect(store.setError).toHaveBeenCalledWith(null);
    expect(store.setSuccess).not.toHaveBeenCalled();
    expect(store.setRefreshFailed).not.toHaveBeenCalled();
  });

  it('navigateToPeriod: selects the month and shows loading while it is fetched', () => {
    const store = buildStore();
    const service = buildService();
    service.getBudgetSummary.mockReturnValue(of(SUMMARY));

    effectsFor(service, store).navigateToPeriod({year: 2027, month: 3});

    expect(store.setSelectedPeriod).toHaveBeenCalledWith(2027, 3);
    expect(store.setLoading).toHaveBeenCalledTimes(1);
    expect(store.setSummary).toHaveBeenCalledWith(SUMMARY);
  });

  it('navigateToPeriod: replaces the page with the error when the month fails', () => {
    const store = buildStore();
    const service = buildService();
    service.getBudgetSummary.mockReturnValue(FAILURE());

    effectsFor(service, store).navigateToPeriod({year: 2027, month: 3});

    expect(store.setError).toHaveBeenCalledTimes(1);
    expect(store.setRefreshFailed).not.toHaveBeenCalled();
  });

  it('saveOverride: creates the override on a line that has none, then reloads the month', () => {
    const store = buildStore();
    const service = buildService();
    service.createBudget.mockReturnValue(of({}));
    service.getBudgetSummary.mockReturnValue(of(SUMMARY));

    effectsFor(service, store).saveOverride({
      category: 'FOOD_AND_DRINK',
      overrideId: null,
      amount: 250,
    });

    expect(service.createBudget).toHaveBeenCalledWith({
      category: 'FOOD_AND_DRINK',
      monthlyLimit: 250,
    });
    expect(service.updateBudget).not.toHaveBeenCalled();
    expect(store.setEditing).toHaveBeenCalledWith(null);
    expect(store.setSummary).toHaveBeenCalledWith(SUMMARY);
  });

  it('saveOverride: updates the override a line already has', () => {
    const store = buildStore();
    const service = buildService();
    service.updateBudget.mockReturnValue(of({}));
    service.getBudgetSummary.mockReturnValue(of(SUMMARY));

    effectsFor(service, store).saveOverride({
      category: 'FOOD_AND_DRINK',
      overrideId: 'o1',
      amount: 300,
    });

    expect(service.updateBudget).toHaveBeenCalledWith('o1', {monthlyLimit: 300});
    expect(service.createBudget).not.toHaveBeenCalled();
  });

  it('resetOverride: deletes the override so the line returns to its suggestion, then reloads', () => {
    const store = buildStore();
    const service = buildService();
    service.deleteBudget.mockReturnValue(of(void 0));
    service.getBudgetSummary.mockReturnValue(of(SUMMARY));

    effectsFor(service, store).resetOverride('o1');

    expect(service.deleteBudget).toHaveBeenCalledWith('o1');
    expect(store.setSummary).toHaveBeenCalledWith(SUMMARY);
  });

  it('saveOverride: keeps the page and does not reload when the save itself fails', () => {
    const store = buildStore();
    const service = buildService();
    service.createBudget.mockReturnValue(FAILURE());

    effectsFor(service, store).saveOverride({
      category: 'FOOD_AND_DRINK',
      overrideId: null,
      amount: 250,
    });

    expect(service.getBudgetSummary).not.toHaveBeenCalled();
    expect(store.setError).not.toHaveBeenCalled();
  });

  it.each([
    [
      'saveOverride',
      (service: ReturnType<typeof buildService>) => service.createBudget.mockReturnValue(of({})),
      (fx: ReturnType<typeof budgetsEffects>) =>
        fx.saveOverride({category: 'FOOD_AND_DRINK', overrideId: null, amount: 100}),
    ],
    [
      'resetOverride',
      (service: ReturnType<typeof buildService>) =>
        service.deleteBudget.mockReturnValue(of(void 0)),
      (fx: ReturnType<typeof budgetsEffects>) => fx.resetOverride('o1'),
    ],
  ])('%s: keeps the page and flags the refresh when it fails afterwards', (_, mock, run) => {
    const store = buildStore();
    const service = buildService();
    mock(service);
    service.getBudgetSummary.mockReturnValue(FAILURE());

    run(effectsFor(service, store));

    expect(store.setRefreshFailed).toHaveBeenCalledTimes(1);
    expect(store.setError).not.toHaveBeenCalled();
    expect(store.setLoading).not.toHaveBeenCalled();
  });
});
