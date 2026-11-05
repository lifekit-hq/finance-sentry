import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type BrokeragePnl} from '../../models/brokerage-pnl/brokerage-pnl.model';
import {BrokeragePnlService} from '../../services/brokerage-pnl.service';
import {BrokeragePnlStore} from './brokerage-pnl.store';

const HTTP_SERVER_ERROR = 500;

const PNL: BrokeragePnl = {
  from: '2026-01-01',
  to: '2026-10-01',
  netDepositedUsd: 1500,
  sourcesWithoutFlows: [],
  realized: {
    usd: 150,
    percentOfNetDeposited: 10,
    status: 'COMPLETE',
    measured: 2,
    total: 2,
    coverage: null,
    unavailableSymbols: [],
  },
  unrealized: {
    usd: 300,
    percentOfNetDeposited: 20,
    status: 'COMPLETE',
    measured: 3,
    total: 3,
    coverage: 1,
    unavailableSymbols: [],
  },
  currency: 'EUR',
};

describe('BrokeragePnlStore', () => {
  const getPnl = vi.fn();

  const create = (response: BrokeragePnl | HttpErrorResponse) => {
    getPnl.mockReset();
    getPnl.mockReturnValue(
      response instanceof HttpErrorResponse ? throwError(() => response) : of(response)
    );
    TestBed.configureTestingModule({
      providers: [BrokeragePnlStore, {provide: BrokeragePnlService, useValue: {getPnl}}],
    });
    return TestBed.inject(BrokeragePnlStore);
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads on init and derives the three rows in the response currency', () => {
    const store = create(PNL);

    expect(getPnl).toHaveBeenCalledOnce();
    expect(store.status()).toBe('success');
    expect(store.rows().map(r => r.key)).toEqual(['net-deposited', 'realized', 'unrealized']);
    expect(store.currency()).toBe('EUR');
  });

  it('has no rows and a dollar currency before the first response arrives', () => {
    getPnl.mockReset();
    getPnl.mockReturnValue(of());
    TestBed.configureTestingModule({
      providers: [BrokeragePnlStore, {provide: BrokeragePnlService, useValue: {getPnl}}],
    });
    const store = TestBed.inject(BrokeragePnlStore);

    expect(store.rows()).toEqual([]);
    expect(store.currency()).toBe('USD');
  });

  it('reports a failed request as an error, not as empty rows', () => {
    const store = create(new HttpErrorResponse({status: HTTP_SERVER_ERROR}));

    expect(store.status()).toBe('error');
    expect(store.errorMessage()).toBe('Failed to load realized and unrealized P&L.');
  });
});
