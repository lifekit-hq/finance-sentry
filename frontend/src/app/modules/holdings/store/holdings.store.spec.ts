import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it} from 'vitest';

import {type Position} from '../models/position/position.model';
import {PositionsService} from '../services/positions.service';
import {QuotesService} from '../services/quotes.service';
import {HoldingsStore} from './holdings.store';

const POSITIONS: Position[] = [
  {
    symbol: 'AAPL',
    provider: 'ibkr',
    instrumentType: 'STK',
    quantity: 1,
    currentValue: 300,
    currentPrice: 300,
    pnlPercent: null,
    pnlUsd: null,
    isVenueCash: false,
    assetClass: 'equity',
  },
  {
    symbol: 'ETH',
    provider: 'binance',
    instrumentType: null,
    quantity: 1,
    currentValue: 100,
    currentPrice: 100,
    pnlPercent: null,
    pnlUsd: null,
    isVenueCash: false,
    assetClass: 'crypto',
  },
];

describe('HoldingsStore provider deep link', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        HoldingsStore,
        {
          provide: PositionsService,
          useValue: {getHoldings: () => of({positions: POSITIONS, headline: null})},
        },
        {provide: QuotesService, useValue: {getQuotes: () => of([])}},
      ],
    });
  });

  it('lists every provider when the URL has no provider', () => {
    const store = TestBed.inject(HoldingsStore);

    expect(store.provider()).toBeNull();
    expect(store.positions()).toHaveLength(2);
  });

  it('opens already filtered to the provider named by ?provider=', async () => {
    await TestBed.inject(Router).navigateByUrl('/?provider=ibkr');

    const store = TestBed.inject(HoldingsStore);

    expect(store.provider()).toBe('ibkr');
    expect(store.positions().map(p => p.symbol)).toEqual(['AAPL']);
    expect(store.totalPositionsValue()).toBe(300);
  });

  it('shows everything again once ?provider= is removed', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/?provider=ibkr');
    const store = TestBed.inject(HoldingsStore);
    expect(store.positions()).toHaveLength(1);

    await router.navigateByUrl('/');
    TestBed.tick();

    expect(store.provider()).toBeNull();
    expect(store.positions()).toHaveLength(2);
  });
});
