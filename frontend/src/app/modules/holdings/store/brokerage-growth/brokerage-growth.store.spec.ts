import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type BrokerageGrowth} from '../../models/brokerage-growth/brokerage-growth.model';
import {BrokerageGrowthService} from '../../services/brokerage-growth.service';
import {BrokerageGrowthStore} from './brokerage-growth.store';

const HTTP_SERVER_ERROR = 500;

const split = (overrides: Partial<BrokerageGrowth> = {}): BrokerageGrowth => ({
  seriesStart: '2026-07-01',
  from: '2026-07-01',
  to: '2026-10-14',
  startValue: 1000,
  endValue: 1210,
  contributed: 100,
  newlyLinked: 0,
  growth: 110,
  income: 15,
  fees: 0,
  taxes: 3,
  moneyWeightedReturn: 0.1,
  sourcesWithoutFlows: [],
  headline: {
    amount: 110,
    percent: 10,
    method: 'MODIFIED_DIETZ',
    scope: 'sleeve:brokerage',
    window: {from: '2026-07-01', to: '2026-10-14'},
    quality: 'COMPLETE',
    reasons: [],
    currency: 'USD',
  },
  currency: 'USD',
  ...overrides,
});

const withheld = (sources: string[]): BrokerageGrowth =>
  split({
    contributed: null,
    newlyLinked: null,
    growth: null,
    moneyWeightedReturn: null,
    sourcesWithoutFlows: sources,
    headline: {...split().headline, amount: null, percent: null, quality: 'UNAVAILABLE'},
  });

describe('BrokerageGrowthStore', () => {
  const getGrowth = vi.fn();

  const create = (response: BrokerageGrowth | HttpErrorResponse) => {
    getGrowth.mockReset();
    getGrowth.mockReturnValue(
      response instanceof HttpErrorResponse ? throwError(() => response) : of(response)
    );
    TestBed.configureTestingModule({
      providers: [BrokerageGrowthStore, {provide: BrokerageGrowthService, useValue: {getGrowth}}],
    });
    return TestBed.inject(BrokerageGrowthStore);
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads on init and shows the split with a signed money-weighted return', () => {
    const store = create(split());

    expect(getGrowth).toHaveBeenCalledOnce();
    expect(store.status()).toBe('success');
    expect(store.hasHistory()).toBe(true);
    expect(store.view()).toBe('split');
    expect(store.returnLabel()).toBe('+10.0%');
    expect(store.showsNewlyLinked()).toBe(false);
  });

  it('signs a loss without a plus', () => {
    const store = create(split({headline: {...split().headline, percent: -3.25}}));

    expect(store.returnLabel()).toBe('-3.3%');
  });

  it('names newly linked value only when there is some', () => {
    const store = create(split({newlyLinked: 510}));

    expect(store.showsNewlyLinked()).toBe(true);
  });

  it('holds the split back while a provider has never reported a deposit', () => {
    const store = create(withheld(['ibkr', 'inzhur']));

    expect(store.view()).toBe('no-flows');
    expect(store.missingProviderLabels()).toBe('Interactive Brokers and Inzhur');
    expect(store.isIbkrMissing()).toBe(true);
    expect(store.returnLabel()).toBeNull();
  });

  it('gives the IBKR Flex hint only when IBKR is the one missing', () => {
    const store = create(withheld(['inzhur']));

    expect(store.isIbkrMissing()).toBe(false);
  });

  it('reads as short history when a split is not yet possible', () => {
    const store = create(withheld([]));

    expect(store.view()).toBe('short-history');
  });

  it('has no history before the brokerage holds any value', () => {
    const store = create(withheld([]));
    expect(store.hasHistory()).toBe(true);

    TestBed.resetTestingModule();
    const empty = create(split({seriesStart: null}));

    expect(empty.hasHistory()).toBe(false);
  });

  it('records a failed load', () => {
    const store = create(new HttpErrorResponse({status: HTTP_SERVER_ERROR, error: {}}));

    expect(store.status()).toBe('error');
    expect(store.errorMessage()).toBe('Failed to load contributions and growth.');
  });
});
