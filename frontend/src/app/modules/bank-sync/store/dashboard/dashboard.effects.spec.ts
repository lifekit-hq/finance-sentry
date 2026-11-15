import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, Subject, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {type HistoryRange} from '../../models/dashboard/dashboard.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {dashboardEffects, dashboardHooks} from './dashboard.effects';

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
    setHistoryScopeChanges: vi.fn(),
    setHistoryChangeHeadline: vi.fn(),
    load: vi.fn(),
    loadNetWorthHistory: vi.fn(),
    historyRange: signal<HistoryRange>('1m'),
  };
}

describe('dashboardHooks auto-refresh', () => {
  const service = {getDashboardData: vi.fn(), getNetWorthHistory: vi.fn()};

  beforeEach(() => {
    vi.useFakeTimers();
    service.getDashboardData.mockReset();
    service.getNetWorthHistory.mockReset();
    TestBed.configureTestingModule({
      providers: [{provide: BankSyncService, useValue: service}],
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
    TestBed.tick();

    vi.advanceTimersByTime(REFRESH_INTERVAL_MS);
    expect(store.setError).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(REFRESH_INTERVAL_MS);
    expect(service.getDashboardData).toHaveBeenCalledTimes(2);
    expect(store.setData).toHaveBeenCalledWith(DATA, '1m');
  });

  it('discards a refresh still in flight when the range changes', () => {
    const base = buildStore();
    const lateRefresh = new Subject<unknown>();
    const history = {snapshots: [], hasHistory: false, scopeChanges: [], changeHeadline: null};
    const rangeData = {range: '1y'};
    service.getNetWorthHistory.mockReturnValue(of(history));
    service.getDashboardData
      .mockReturnValueOnce(of(DATA))
      .mockReturnValueOnce(lateRefresh)
      .mockReturnValueOnce(of(rangeData));

    TestBed.runInInjectionContext(() => {
      const effects = dashboardEffects(base);
      dashboardHooks({...base, ...effects});
    });
    TestBed.tick();
    expect(base.setData).toHaveBeenLastCalledWith(DATA, '1m');

    vi.advanceTimersByTime(REFRESH_INTERVAL_MS);
    expect(service.getDashboardData).toHaveBeenCalledTimes(2);

    base.historyRange.set('1y');
    TestBed.tick();
    expect(base.setData).toHaveBeenLastCalledWith(rangeData, '1y');

    lateRefresh.next({range: '1m'});

    expect(base.setData).toHaveBeenCalledTimes(2);
    expect(base.setData).toHaveBeenLastCalledWith(rangeData, '1y');
  });
});

describe('dashboardEffects reload', () => {
  const service = {getDashboardData: vi.fn(), getNetWorthHistory: vi.fn()};

  beforeEach(() => {
    service.getDashboardData.mockReset().mockReturnValue(of(DATA));
    service.getNetWorthHistory
      .mockReset()
      .mockReturnValue(
        of({snapshots: [], hasHistory: false, scopeChanges: [], changeHeadline: null})
      );
    TestBed.configureTestingModule({
      providers: [{provide: BankSyncService, useValue: service}],
    });
  });

  it('fetches the statistics and the history again for the range on screen', () => {
    const store = buildStore();
    store.historyRange.set('1y');
    const effects = TestBed.runInInjectionContext(() => dashboardEffects(store));

    effects.reload();

    expect(service.getNetWorthHistory).toHaveBeenCalledTimes(1);
    expect(service.getNetWorthHistory).toHaveBeenCalledWith('1y');
    expect(service.getDashboardData).toHaveBeenCalledTimes(1);
    expect(store.setLoading).toHaveBeenCalledTimes(1);
    expect(store.setHistoryLoading).toHaveBeenCalledWith(true);
    expect(store.setData).toHaveBeenCalledWith(DATA, '1y');
    expect(store.setHistoryLoading).toHaveBeenLastCalledWith(false);
  });

  it('keeps the history loading while only the statistics have landed', () => {
    const store = buildStore();
    const lateHistory = new Subject<unknown>();
    service.getNetWorthHistory.mockReturnValue(lateHistory);
    const effects = TestBed.runInInjectionContext(() => dashboardEffects(store));

    effects.reload();

    expect(store.setSuccess).toHaveBeenCalledTimes(1);
    expect(store.setHistoryLoading).toHaveBeenCalledWith(true);
    expect(store.setHistoryLoading).not.toHaveBeenCalledWith(false);
  });
});
