import {provideHttpClient, withXhr} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {API_BASE_URL} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {afterEach, beforeEach, describe, expect, it} from 'vitest';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {
  type BrokerageHoldingsDto,
  type CryptoHoldingsDto,
  type Position,
} from '../models/position/position.model';
import {type HoldingsResult, PositionsService} from './positions.service';

const BASE = 'http://api.test';

const emptyBrokerage: BrokerageHoldingsDto = {
  provider: 'ibkr',
  syncedAt: null,
  isStale: false,
  totalUsdValue: 0,
  positions: [],
};

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

  it('prices a crypto unit over free plus locked quantity and maps the venue average', () => {
    const crypto: CryptoHoldingsDto = {
      provider: 'multiple',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 9_200,
      holdings: [
        {
          asset: 'ETH',
          freeQuantity: 1.5,
          lockedQuantity: 0.5,
          usdValue: 8_000,
          costBasisUsd: 6_000,
          averageBuyPriceUsd: 3_000,
          provider: 'binance',
          isFiat: false,
        },
        {
          asset: 'SOL',
          freeQuantity: 6,
          lockedQuantity: 0,
          usdValue: 1_200,
          costBasisUsd: 900,
          averageBuyPriceUsd: null,
          provider: 'binance',
          isFiat: false,
        },
        {
          asset: 'BTC',
          freeQuantity: 0,
          lockedQuantity: 0,
          usdValue: 0,
          provider: 'revolut_x',
          isFiat: false,
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(null);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(crypto);

    expect(positions.map(p => [p.symbol, p.currentPrice, p.averagePrice])).toEqual([
      ['ETH', 4_000, 3_000],
      ['SOL', 200, 150],
      ['BTC', null, null],
    ]);
  });

  it('states no average for a crypto row without trade history, and none for venue cash', () => {
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
          costBasisUsd: null,
          averageBuyPriceUsd: null,
          provider: 'revolut_x',
          isFiat: false,
        },
        {
          asset: 'EUR',
          freeQuantity: 1_000,
          lockedQuantity: 0,
          usdValue: 1_080,
          costBasisUsd: 1_000,
          averageBuyPriceUsd: 1,
          provider: 'revolut_x',
          isFiat: true,
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(null);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(crypto);

    expect(positions.map(p => [p.symbol, p.currentPrice, p.averagePrice])).toEqual([
      ['BTC', 60_000, null],
      ['EUR', null, null],
    ]);
  });

  it("prices a broker unit from its value and takes the broker's average cost", () => {
    const brokerage: BrokerageHoldingsDto = {
      provider: 'ibkr',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 0,
      positions: [
        {
          symbol: 'AAPL',
          instrumentType: 'STK',
          quantity: 10,
          usdValue: 2_000,
          costBasisUsd: 1_500,
          averageCostUsd: 150,
          provider: 'ibkr',
        },
        {
          symbol: 'IZH',
          instrumentType: 'REIT',
          quantity: 4,
          usdValue: 400,
          costBasisUsd: 360,
          averageCostUsd: null,
          provider: 'inzhur',
        },
        {
          symbol: 'MSFT',
          instrumentType: 'STK',
          quantity: 5,
          usdValue: 2_000,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'ibkr',
        },
        {
          symbol: 'GONE',
          instrumentType: 'STK',
          quantity: 0,
          usdValue: 0,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'ibkr',
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(brokerage);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(null);

    expect(positions.map(p => [p.symbol, p.currentPrice, p.averagePrice])).toEqual([
      ['AAPL', 200, 150],
      ['IZH', 100, 90],
      ['MSFT', 400, null],
      ['GONE', null, null],
    ]);
  });

  it('tags broker rows with the backend asset class, cash included, defaulting to equity', () => {
    const brokerage: BrokerageHoldingsDto = {
      provider: 'mixed',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 0,
      positions: [
        {
          symbol: 'USD Cash',
          instrumentType: 'CASH',
          assetClass: 'Cash',
          quantity: 100,
          usdValue: 100,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'ibkr',
        },
        {
          symbol: 'UAH Cash',
          instrumentType: 'CASH',
          assetClass: 'Cash',
          quantity: 4_000,
          usdValue: 100,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'inzhur',
        },
        {
          symbol: 'AAPL',
          instrumentType: 'STK',
          assetClass: 'Equities',
          quantity: 1,
          usdValue: 200,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'ibkr',
        },
        {
          symbol: 'WAR',
          instrumentType: 'BOND',
          assetClass: 'Bonds',
          quantity: 1,
          usdValue: 50,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'inzhur',
        },
        {
          symbol: 'OLD',
          instrumentType: 'STK',
          quantity: 1,
          usdValue: 10,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'ibkr',
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(brokerage);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(null);

    expect(positions.map(p => [p.symbol, p.assetClass])).toEqual([
      ['USD Cash', 'cash'],
      ['UAH Cash', 'cash'],
      ['AAPL', 'equity'],
      ['WAR', 'bonds'],
      ['OLD', 'equity'],
    ]);
  });
  it('keeps a non-USD broker row in its own currency next to the USD value', () => {
    const brokerage: BrokerageHoldingsDto = {
      provider: 'mixed',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 130,
      positions: [
        {
          symbol: 'UAH Cash',
          instrumentType: 'CASH',
          quantity: 5_000,
          usdValue: 120,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'inzhur',
          assetClass: 'Cash',
          currency: 'UAH',
          nativeValue: 5_000,
        },
        {
          symbol: 'AAPL',
          instrumentType: 'STK',
          quantity: 1,
          usdValue: 10,
          costBasisUsd: null,
          averageCostUsd: null,
          provider: 'ibkr',
          assetClass: 'Equities',
          currency: 'USD',
          nativeValue: null,
        },
      ],
    };

    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(brokerage);
    http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(null);

    expect(positions.map(p => [p.symbol, p.currentValue, p.nativeCurrency, p.nativeValue])).toEqual(
      [
        ['UAH Cash', 120, 'UAH', 5_000],
        ['AAPL', 10, undefined, undefined],
      ]
    );
  });

  describe('getHoldings headline', () => {
    const feed = (usdValue: number): CryptoHoldingsDto => ({
      provider: 'binance',
      syncedAt: null,
      isStale: false,
      totalUsdValue: usdValue,
      holdings: [
        {
          asset: 'BTC',
          freeQuantity: 1,
          lockedQuantity: 0,
          usdValue,
          provider: 'binance',
          isFiat: false,
        },
      ],
    });
    const load = (
      brokerage: Nullable<BrokerageHoldingsDto>,
      crypto: Nullable<CryptoHoldingsDto>
    ): HoldingsResult => {
      let result: Nullable<HoldingsResult> = null;
      service.getHoldings().subscribe(r => (result = r));
      http.match(req => req.url.endsWith('brokerage/holdings'))[0].flush(brokerage);
      http.match(req => req.url.endsWith('crypto/holdings'))[0].flush(crypto);
      return result as unknown as HoldingsResult;
    };

    it('has no headline when both feeds read and the API sent none', () => {
      expect(load({...emptyBrokerage}, feed(100)).headline).toBeNull();
    });

    it("combines the feeds' own records", () => {
      const record = (amount: number): Headline => ({
        amount,
        percent: null,
        method: 'VALUE',
        basis: 'MARKET_VALUE',
        scope: 'sleeve:invested',
        window: {from: null, to: '2026-10-15'},
        quality: 'ESTIMATED',
        basisStatus: 'COMPLETE',
        coverage: 1,
        reasons: [{code: 'FX_TODAY_RATE'}],
        asOf: '2026-10-15T08:00:00Z',
        currency: 'USD',
      });
      const result = load(
        {...emptyBrokerage, headline: record(10)},
        {...feed(100), headline: record(100)}
      );

      expect(result.headline).toMatchObject({amount: 110, quality: 'ESTIMATED'});
    });

    it('names a failed feed as a stale source and keeps the other one', () => {
      const result = load(null, feed(100));

      expect(result.positions).toHaveLength(1);
      expect(result.headline).toMatchObject({
        amount: 100,
        quality: 'DEGRADED',
        reasons: [{code: 'STALE_SOURCE', source: 'brokerage'}],
      });
    });

    it('is unavailable, not zero, when every feed failed', () => {
      const result = load(null, null);

      expect(result.positions).toEqual([]);
      expect(result.headline).toMatchObject({amount: null, quality: 'UNAVAILABLE'});
      expect(result.headline?.reasons.map(r => r.source)).toEqual(['brokerage', 'crypto']);
    });
  });

  it('carries the API hidden verdict of crypto and brokerage rows onto the positions', () => {
    let positions: Position[] = [];
    service.getPositions().subscribe(p => (positions = p));
    http.expectOne(`${BASE}/brokerage/holdings`).flush({
      ...emptyBrokerage,
      positions: [
        {
          symbol: 'DUST',
          instrumentType: 'STK',
          quantity: 1,
          usdValue: 0.5,
          costBasisUsd: null,
          averageCostUsd: null,
          hiddenReason: 'small',
        },
        {
          symbol: 'AAPL',
          instrumentType: 'STK',
          quantity: 1,
          usdValue: 200,
          costBasisUsd: null,
          averageCostUsd: null,
        },
      ],
    });
    http.expectOne(`${BASE}/crypto/holdings`).flush({
      provider: 'binance',
      syncedAt: null,
      isStale: false,
      totalUsdValue: 0.1,
      holdings: [
        {
          asset: 'SHIB',
          freeQuantity: 1,
          lockedQuantity: 0,
          usdValue: 0.1,
          provider: 'binance',
          isFiat: false,
          hiddenReason: 'small',
        },
      ],
    });

    expect(positions.map(p => [p.symbol, p.hiddenReason])).toEqual([
      ['DUST', 'small'],
      ['AAPL', null],
      ['SHIB', 'small'],
    ]);
  });
});
