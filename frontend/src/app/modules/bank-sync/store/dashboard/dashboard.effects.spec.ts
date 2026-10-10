import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, Router} from '@angular/router';
import {of, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type HistoryRange} from '../../models/dashboard/dashboard.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {dashboardHooks} from './dashboard.effects';

const REFRESH_INTERVAL_MS = 300_000;
const DATA = {refreshed: true};

function buildStore() {
  return {
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    setError: vi.fn(),
    setData: vi.fn(),
    setNetWorthHistory: vi.fn(),
    setHistoryLoading: vi.fn(),
    setHistoryError: vi.fn(),
    setHistoryHasHistory: vi.fn(),
    setHistoryRange: vi.fn(),
    load: vi.fn(),
    loadNetWorthHistory: vi.fn(),
    historyRange: signal<HistoryRange>('1m'),
  };
}

describe('dashboardHooks auto-refresh', () => {
  const service = {getDashboardData: vi.fn()};

  beforeEach(() => {
    vi.useFakeTimers();
    service.getDashboardData.mockReset();
    TestBed.configureTestingModule({
      providers: [
        {provide: BankSyncService, useValue: service},
        {provide: Router, useValue: {navigate: vi.fn()}},
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: {get: () => null}}}},
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('keeps refreshing after one failed refresh', () => {
    const store = buildStore();
    service.getDashboardData
      .mockReturnValueOnce(
        throwError(() => new HttpErrorResponse({status: 400, error: {errorCode: 'DOWN'}}))
      )
      .mockReturnValue(of(DATA));

    TestBed.runInInjectionContext(() => dashboardHooks(store));

    vi.advanceTimersByTime(REFRESH_INTERVAL_MS);
    expect(store.setError).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(REFRESH_INTERVAL_MS);
    expect(service.getDashboardData).toHaveBeenCalledTimes(2);
    expect(store.setData).toHaveBeenCalledWith(DATA);
  });
});
