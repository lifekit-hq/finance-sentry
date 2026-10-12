import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type BudgetSummaryResponse} from '../../models/budget/budget.model';
import {BudgetsService} from '../../services/budgets.service';
import {budgetsEffects} from './budgets.effects';

const SUMMARY: BudgetSummaryResponse = {
  year: 2026,
  month: 9,
  items: [],
  totalLimit: 0,
  totalSpent: 0,
};
const FAILURE = () => throwError(() => new HttpErrorResponse({status: 500}));

function buildStore() {
  return {
    setSummaryItems: vi.fn(),
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    clearRefreshFailed: vi.fn(),
    setError: vi.fn(),
    setRefreshFailed: vi.fn(),
    setEditing: vi.fn(),
    updateBudgetInList: vi.fn(),
    removeBudget: vi.fn(),
    setSelectedPeriod: vi.fn(),
    selectedYear: () => 2026,
    selectedMonth: () => 9,
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
    expect(store.setSummaryItems).toHaveBeenCalledWith(SUMMARY.items);
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

    effectsFor(service, store).navigateToPeriod({year: 2026, month: 8});

    expect(store.setSelectedPeriod).toHaveBeenCalledWith(2026, 8);
    expect(store.setLoading).toHaveBeenCalledTimes(1);
    expect(store.setSummaryItems).toHaveBeenCalledWith(SUMMARY.items);
  });

  it('navigateToPeriod: replaces the page with the error when the month fails', () => {
    const store = buildStore();
    const service = buildService();
    service.getBudgetSummary.mockReturnValue(FAILURE());

    effectsFor(service, store).navigateToPeriod({year: 2026, month: 8});

    expect(store.setError).toHaveBeenCalledTimes(1);
    expect(store.setRefreshFailed).not.toHaveBeenCalled();
  });

  it.each([
    [
      'create',
      (service: ReturnType<typeof buildService>) => service.createBudget.mockReturnValue(of({})),
      (fx: ReturnType<typeof budgetsEffects>) =>
        fx.create({category: 'groceries', monthlyLimit: 100}),
    ],
    [
      'update',
      (service: ReturnType<typeof buildService>) => service.updateBudget.mockReturnValue(of({})),
      (fx: ReturnType<typeof budgetsEffects>) => fx.update({id: 'b1', monthlyLimit: 100}),
    ],
    [
      'remove',
      (service: ReturnType<typeof buildService>) =>
        service.deleteBudget.mockReturnValue(of(void 0)),
      (fx: ReturnType<typeof budgetsEffects>) => fx.remove('b1'),
    ],
  ])('%s: keeps the list and flags the refresh when it fails afterwards', (_, mock, run) => {
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
