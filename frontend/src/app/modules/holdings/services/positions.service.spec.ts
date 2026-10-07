import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {
  type BrokerageHoldingsDto,
  type CryptoHoldingsDto,
  type Position,
} from '../models/position/position.model';
import {PositionsService} from './positions.service';

const BASE = 'http://api.test';

describe('PositionsService', () => {
  let service: PositionsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {provide: API_BASE_URL, useValue: BASE},
      ],
    });
    service = TestBed.inject(PositionsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('labels each crypto position with the venue it is held on', () => {
    const crypto: CryptoHoldingsDto = {
      provider: 'multiple',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 36_000,
      holdings: [
        {
          asset: 'BTC',
          freeQuantity: 0.5,
          lockedQuantity: 0,
          usdValue: 30_000,
          provider: 'binance',
          isFiat: false,
        },
        {
          asset: 'BTC',
          freeQuantity: 0.1,
          lockedQuantity: 0,
          usdValue: 6_000,
          provider: 'revolut_x',
          isFiat: false,
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(null);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(crypto);

    expect(positions.map(p => [p.symbol, p.provider, p.currentValue])).toEqual([
      ['BTC', 'binance', 30_000],
      ['BTC', 'revolut_x', 6_000],
    ]);
    expect(positions.every(p => p.pnlPercent === null)).toBe(true);
  });

  it('labels each brokerage position with its broker when holdings span several', () => {
    const brokerage: BrokerageHoldingsDto = {
      provider: 'mixed',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 1_250,
      positions: [
        {
          symbol: 'AAPL',
          instrumentType: 'STK',
          quantity: 1,
          usdValue: 1_000,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'ibkr',
        },
        {
          symbol: 'Fund A',
          instrumentType: 'REIT',
          quantity: 10,
          usdValue: 250,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'inzhur',
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(brokerage);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(null);

    expect(positions.map(p => [p.symbol, p.provider, p.instrumentType])).toEqual([
      ['AAPL', 'ibkr', 'STK'],
      ['Fund A', 'inzhur', 'REIT'],
    ]);
  });

  it('marks venue fiat as venue cash with no unit price', () => {
    const crypto: CryptoHoldingsDto = {
      provider: 'revolut_x',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 7_080,
      holdings: [
        {
          asset: 'BTC',
          freeQuantity: 0.1,
          lockedQuantity: 0,
          usdValue: 6_000,
          provider: 'revolut_x',
          isFiat: false,
        },
        {
          asset: 'EUR',
          freeQuantity: 1_000,
          lockedQuantity: 0,
          usdValue: 1_080,
          provider: 'revolut_x',
          isFiat: true,
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(null);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(crypto);

    expect(positions.map(p => [p.symbol, p.isVenueCash, p.currentPrice, p.quantity])).toEqual([
      ['BTC', false, 60_000, 0.1],
      ['EUR', true, null, 1_000],
    ]);
  });
});
