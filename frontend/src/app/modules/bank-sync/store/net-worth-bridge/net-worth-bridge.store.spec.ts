import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {provideErrorMessages} from '../../../../core/providers/error-messages.provider';
import {type HistoryRange} from '../../models/dashboard/dashboard.model';
import {type NetWorthBridgeResponse} from '../../models/net-worth-bridge/net-worth-bridge.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {DashboardStore} from '../dashboard/dashboard.store';
import {NetWorthBridgeStore} from './net-worth-bridge.store';

const HTTP_SERVER_ERROR = 500;

const bridge = (overrides: Partial<NetWorthBridgeResponse> = {}): NetWorthBridgeResponse => ({
  from: '2026-09-01',
  to: '2026-10-01',
  startValue: 10_000,
  endValue: 11_000,
  savings: 600,
  investmentReturn: 300,
  market: 300,
  income: 0,
  fees: 0,
  taxes: 0,
  fxTranslation: 100,
  revaluation: 0,
  newlyLinked: 0,
  unlinked: 0,
  residual: 0,
  residualAboveTolerance: false,
  quality: 'COMPLETE',
  reasons: [],
  currency: 'USD',
  ...overrides,
});

const unavailable = (reasons = [{code: 'SHORT_HISTORY'}]): NetWorthBridgeResponse =>
  bridge({
    startValue: null,
    endValue: null,
    savings: null,
    investmentReturn: null,
    fxTranslation: null,
    residual: null,
    quality: 'UNAVAILABLE',
    reasons,
  });

describe('NetWorthBridgeStore', () => {
  const getNetWorthBridge = vi.fn();
  let range: ReturnType<typeof signal<HistoryRange>>;

  const create = (response: NetWorthBridgeResponse | HttpErrorResponse) => {
    getNetWorthBridge.mockReset();
    getNetWorthBridge.mockReturnValue(
      response instanceof HttpErrorResponse ? throwError(() => response) : of(response)
    );
    range = signal<HistoryRange>('1m');
    TestBed.configureTestingModule({
      providers: [
        provideErrorMessages(),
        NetWorthBridgeStore,
        {provide: BankSyncService, useValue: {getNetWorthBridge}},
        {provide: DashboardStore, useValue: {historyRange: range}},
      ],
    });
    const store = TestBed.inject(NetWorthBridgeStore);
    TestBed.tick();
    return store;
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads the bridge over the dashboard range and draws its rows', () => {
    const store = create(bridge());

    expect(getNetWorthBridge).toHaveBeenCalledExactlyOnceWith('1m');
    expect(store.status()).toBe('success');
    expect(store.steps().map(s => s.key)).toEqual([
      'start',
      'savings',
      'investmentReturn',
      'fxTranslation',
      'residual',
      'end',
    ]);
    expect(store.headline()).toMatchObject({amount: 1_000, method: 'BRIDGE', quality: 'COMPLETE'});
    expect(store.unavailableMessage()).toBeNull();
    expect(store.degraded()).toBeNull();
  });

  it('reloads when the dashboard range changes', () => {
    create(bridge());

    range.set('1y');
    TestBed.tick();

    expect(getNetWorthBridge).toHaveBeenLastCalledWith('1y');
  });

  it('retries over the range on screen', () => {
    const store = create(bridge());
    range.set('3m');
    TestBed.tick();

    store.retry();

    expect(getNetWorthBridge).toHaveBeenCalledTimes(3);
    expect(getNetWorthBridge).toHaveBeenLastCalledWith('3m');
  });

  it('names the residual above tolerance as the DEGRADED reason, with its size', () => {
    const store = create(
      bridge({
        residual: 250,
        residualAboveTolerance: true,
        quality: 'DEGRADED',
        reasons: [{code: 'RESIDUAL_ABOVE_TOLERANCE'}],
      })
    );

    expect(store.degraded()).toEqual({
      reason:
        'The change in net worth is not fully explained by known movements and price changes.',
      detail:
        '+$250.00 is unexplained, more than 0.2% of the starting value, so read the lines above it as approximate.',
    });
  });

  it('reads an unavailable bridge reason instead of drawing rows', () => {
    const store = create(unavailable());

    expect(store.steps()).toEqual([]);
    expect(store.headline()?.amount).toBeNull();
    expect(store.unavailableMessage()).toBe(
      'There is not enough history yet to measure this period.'
    );
  });

  it('falls back to its own line for an unavailable bridge with no known reason', () => {
    const store = create(unavailable([]));

    expect(store.unavailableMessage()).toBe(
      'Not enough history in this period to explain the change yet.'
    );
  });

  it('records a failed load', () => {
    const store = create(new HttpErrorResponse({status: HTTP_SERVER_ERROR, error: {}}));

    expect(store.status()).toBe('error');
    expect(store.errorMessage()).toBe('Failed to load why your net worth changed.');
    expect(store.headline()).toBeNull();
    expect(store.steps()).toEqual([]);
    expect(store.unavailableMessage()).toBeNull();
    expect(store.degraded()).toBeNull();
  });
});
