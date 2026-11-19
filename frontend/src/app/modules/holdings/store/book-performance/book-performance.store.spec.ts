import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type BookPerformance,
  type BookPerformancePeriod,
} from '../../models/book-performance/book-performance.model';
import {
  type TrackRecord,
  type TrackRecordRow,
} from '../../models/book-performance/track-record.model';
import {BookPerformanceService} from '../../services/book-performance.service';
import {TrackRecordService} from '../../services/track-record.service';
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

const EMPTY_TRACK_RECORD: TrackRecord = {rule: {window: 'ThreeMonths'}, rows: []};

const row = (overrides: Partial<TrackRecordRow> = {}): TrackRecordRow => ({
  scope: 'Sleeve',
  label: 'Equities',
  window: 'ThreeMonths',
  sustainedUnderperformance: true,
  ...overrides,
});

describe('BookPerformanceStore', () => {
  const getPerformance = vi.fn();
  const getTrackRecord = vi.fn();

  const create = (
    response: BookPerformance | HttpErrorResponse,
    trackRecord: TrackRecord | HttpErrorResponse = EMPTY_TRACK_RECORD
  ) => {
    getPerformance.mockReset();
    getTrackRecord.mockReset();
    getTrackRecord.mockReturnValue(
      trackRecord instanceof HttpErrorResponse ? throwError(() => trackRecord) : of(trackRecord)
    );
    getPerformance.mockReturnValue(
      response instanceof HttpErrorResponse ? throwError(() => response) : of(response)
    );
    TestBed.configureTestingModule({
      providers: [
        BookPerformanceStore,
        {provide: BookPerformanceService, useValue: {getPerformance}},
        {provide: TrackRecordService, useValue: {getTrackRecord}},
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
    expect(comparison?.qqqReturn).toBeNull();
  });

  it('reads QQQ over the same window when it was measured', () => {
    const store = create(performance([period({qqqTwr: 0.145, qqqDelta: -0.025})]));

    expect(store.comparison()?.qqqReturn).toBe('+14.5%');
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

  it('reads the 3M period beside the longest window', () => {
    const store = create(
      performance([period({period: 2, bookTwr: -0.164, spyTwr: -0.023}), period({period: 3})])
    );

    expect(store.shortComparison()).toEqual({bookReturn: '-16.4%', benchmarkReturn: '-2.3%'});
  });

  it('has no 3M figure when that window was not measured for both', () => {
    const store = create(performance([period({period: 2, spyTwr: null}), period({period: 3})]));

    expect(store.shortComparison()).toBeNull();
  });

  it('has no 3M figure when SPY was not measured over the same dates as the book', () => {
    const store = create(
      performance([
        period({period: 2, bookTwr: -0.164, spyTwr: -0.023, delta: null}),
        period({period: 3}),
      ])
    );

    expect(store.shortComparison()).toBeNull();
  });

  it('flags the sleeve the stored record marks as sustained underperformance in the rule window', () => {
    const store = create(performance([period()]), {
      ...EMPTY_TRACK_RECORD,
      rows: [row({scope: 'Thesis', label: 'AAPL'}), row()],
    });

    expect(store.sustained()).toEqual({label: 'Equities'});
  });

  it('does not flag a row outside the rule window or one the job left unflagged', () => {
    const store = create(performance([period()]), {
      ...EMPTY_TRACK_RECORD,
      rows: [
        row({window: 'OneYear'}),
        row({sustainedUnderperformance: false}),
        row({scope: 'Thesis'}),
      ],
    });

    expect(store.sustained()).toBeNull();
  });

  it('keeps the figures and drops only the flag when the track record cannot be read', () => {
    const store = create(
      performance([period()]),
      new HttpErrorResponse({status: HTTP_SERVER_ERROR})
    );

    expect(store.status()).toBe('success');
    expect(store.comparison()).not.toBeNull();
    expect(store.sustained()).toBeNull();
  });
});
