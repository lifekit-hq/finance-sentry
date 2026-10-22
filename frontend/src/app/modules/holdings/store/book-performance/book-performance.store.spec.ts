import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type BookPerformance,
  type BookPerformancePeriod,
} from '../../models/book-performance/book-performance.model';
import {BookPerformanceService} from '../../services/book-performance.service';
import {BookPerformanceStore} from './book-performance.store';

const HTTP_SERVER_ERROR = 500;

const period = (overrides: Partial<BookPerformancePeriod> = {}): BookPerformancePeriod => ({
  bookTwr: 0.12,
  spyTwr: 0.1,
  delta: 0.02,
  bookTwrFrom: '2025-10-19',
  bookTwrTo: '2026-10-19',
  bookTwrReasons: [],
  bookTwrQuality: 'COMPLETE',
  ...overrides,
});

const performance = (periods: BookPerformancePeriod[]): BookPerformance => ({
  periods,
  asOf: '2026-10-19',
});

describe('BookPerformanceStore', () => {
  const getPerformance = vi.fn();

  const create = (response: BookPerformance | HttpErrorResponse) => {
    getPerformance.mockReset();
    getPerformance.mockReturnValue(
      response instanceof HttpErrorResponse ? throwError(() => response) : of(response)
    );
    TestBed.configureTestingModule({
      providers: [
        BookPerformanceStore,
        {provide: BookPerformanceService, useValue: {getPerformance}},
      ],
    });
    return TestBed.inject(BookPerformanceStore);
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('compares the book with SPY over the longest window measured for both', () => {
    const store = create(
      performance([period({bookTwr: 0.01, bookTwrFrom: '2026-09-19'}), period()])
    );

    expect(getPerformance).toHaveBeenCalledOnce();
    expect(store.status()).toBe('success');
    const comparison = store.comparison();
    expect(comparison?.from).toBe('2025-10-19');
    expect(comparison?.bookReturn).toBe('+12.0%');
    expect(comparison?.benchmarkReturn).toBe('+10.0%');
  });

  it('skips a window where SPY was not measured over the book dates', () => {
    const store = create(
      performance([period({bookTwrFrom: '2026-09-19'}), period({spyTwr: null, delta: null})])
    );

    expect(store.comparison()?.from).toBe('2026-09-19');
  });

  it('carries the book return quality and reasons as a TWR headline', () => {
    const reasons = [{code: 'UNKNOWN_FLOW', source: 'ibkr'}];
    const store = create(
      performance([period({bookTwrQuality: 'ESTIMATED', bookTwrReasons: reasons})])
    );

    expect(store.comparison()?.headline).toEqual({
      amount: null,
      percent: 12,
      method: 'TWR',
      scope: 'sleeve:brokerage',
      window: {from: '2025-10-19', to: '2026-10-19'},
      quality: 'ESTIMATED',
      reasons,
      currency: 'USD',
    });
  });

  it('wears no badge when an older API build sends no quality', () => {
    const store = create(performance([period({bookTwrQuality: undefined, bookTwrReasons: null})]));

    expect(store.comparison()?.headline).toBeNull();
  });

  it('has nothing to show when no window is measured for both', () => {
    const store = create(performance([period({bookTwr: null}), period({bookTwrFrom: null})]));

    expect(store.comparison()).toBeNull();
  });

  it('records a failed load', () => {
    const store = create(new HttpErrorResponse({status: HTTP_SERVER_ERROR, error: {}}));

    expect(store.status()).toBe('error');
    expect(store.errorMessage()).toBe('Failed to load the book against SPY.');
    expect(store.comparison()).toBeNull();
  });
});
