import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {describe, expect, it} from 'vitest';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {type Position} from '../models/position/position.model';
import {holdingsComputed} from './holdings.computed';

function position(overrides: Partial<Position>): Position {
  return {
    symbol: 'BTC',
    provider: 'revolut_x',
    instrumentType: null,
    quantity: 1,
    currentValue: 0,
    currentPrice: 0,
    pnlPercent: null,
    pnlUsd: null,
    isVenueCash: false,
    assetClass: 'crypto',
    ...overrides,
  };
}

function build(
  positions: Position[],
  dayChangePctByTicker: Record<string, number> = {},
  status: AsyncStatus = 'idle',
  headline: Nullable<Headline> = null,
  provider: Nullable<string> = null
) {
  return TestBed.runInInjectionContext(() =>
    holdingsComputed({
      positions: signal(positions),
      headline: signal(headline),
      status: signal<AsyncStatus>(status),
      dayChangePctByTicker: signal(dayChangePctByTicker),
      provider: signal(provider),
    })
  );
}

describe('holdingsComputed headline', () => {
  const headline = (overrides: Partial<Headline>): Headline => ({
    amount: 100,
    percent: null,
    method: 'VALUE',
    scope: 'umbrella',
    window: {to: '2026-10-15'},
    quality: 'COMPLETE',
    reasons: [],
    currency: 'USD',
    ...overrides,
  });

  it('carries the record and is available by default', () => {
    const c = build([], {}, 'success', null);

    expect(c.totalHeadline()).toBeNull();
    expect(c.isTotalUnavailable()).toBe(false);
  });

  it('flags an unavailable total so the header reads N/A', () => {
    const unavailable = headline({
      amount: null,
      quality: 'UNAVAILABLE',
      reasons: [{code: 'STALE_SOURCE', source: 'crypto'}],
    });
    const c = build([], {}, 'success', unavailable);

    expect(c.totalHeadline()).toBe(unavailable);
    expect(c.isTotalUnavailable()).toBe(true);
  });

  it('is empty only when there are no positions and the headline needs no badge', () => {
    expect(build([], {}, 'success', null).isEmpty()).toBe(true);
    expect(build([], {}, 'success', headline({})).isEmpty()).toBe(true);
    expect(build([position({})], {}, 'success', null).isEmpty()).toBe(false);
  });

  it('keeps the total card when a failed feed leaves no positions', () => {
    const stale = headline({
      quality: 'DEGRADED',
      reasons: [{code: 'STALE_SOURCE', source: 'brokerage'}],
    });
    const unavailable = headline({amount: null, quality: 'UNAVAILABLE'});

    expect(build([], {}, 'success', stale).isEmpty()).toBe(false);
    expect(build([], {}, 'success', unavailable).isEmpty()).toBe(false);
  });
});

describe('holdingsComputed', () => {
  it('positionsLoaded is false before the first load and true once it succeeds, even when empty', () => {
    expect(build([]).positionsLoaded()).toBe(false);
    expect(build([], {}, 'success').positionsLoaded()).toBe(true);
  });

  it('groups venue fiat as venue cash, apart from the crypto held on the same venue', () => {
    const computed = build([
      position({symbol: 'BTC', currentValue: 6_000, currentPrice: 60_000, quantity: 0.1}),
      position({
        symbol: 'EUR',
        currentValue: 2_000,
        currentPrice: null,
        quantity: 1_850,
        isVenueCash: true,
        assetClass: 'cash',
      }),
      position({
        symbol: 'AAPL',
        provider: 'ibkr',
        currentValue: 2_000,
        currentPrice: 200,
        assetClass: 'equity',
      }),
    ]);

    const groups = computed.positionsByAssetClass();

    expect(groups.map(g => [g.assetClass, g.label, g.totalValue])).toEqual([
      ['cash', 'Cash', 2_000],
      ['equity', 'Equities', 2_000],
      ['crypto', 'Crypto', 6_000],
    ]);
    expect(groups[2].rows.map(r => r.symbol)).toEqual(['BTC']);
    expect(groups[0].rows).toEqual([
      expect.objectContaining({
        symbol: 'EUR',
        provider: 'revolut_x',
        currentPrice: null,
        weightPercent: 20,
      }),
    ]);
    expect(computed.allocationBreakdown().map(r => [r.label, r.percent])).toEqual([
      ['Cash', 20],
      ['Equities', 20],
      ['Crypto', 60],
    ]);
  });

  it('puts broker cash in the Cash slice with venue cash, out of the Equities weights', () => {
    const computed = build([
      position({
        symbol: 'USD Cash',
        provider: 'ibkr',
        instrumentType: 'CASH',
        currentValue: 300,
        assetClass: 'cash',
      }),
      position({
        symbol: 'UAH Cash',
        provider: 'inzhur',
        instrumentType: 'CASH',
        currentValue: 100,
        assetClass: 'cash',
      }),
      position({
        symbol: 'EUR',
        currentValue: 100,
        currentPrice: null,
        isVenueCash: true,
        assetClass: 'cash',
      }),
      position({
        symbol: 'AAPL',
        provider: 'ibkr',
        instrumentType: 'STK',
        currentValue: 400,
        assetClass: 'equity',
      }),
      position({
        symbol: 'UA-BOND',
        provider: 'inzhur',
        instrumentType: 'BOND',
        currentValue: 100,
        assetClass: 'bonds',
      }),
      position({
        symbol: 'REIT',
        provider: 'inzhur',
        instrumentType: 'REIT',
        currentValue: 100,
        assetClass: 'realEstate',
      }),
    ]);

    const groups = computed.positionsByAssetClass();

    expect(groups.map(g => [g.label, g.totalValue])).toEqual([
      ['Cash', 500],
      ['Bonds', 100],
      ['Real estate', 100],
      ['Equities', 400],
    ]);
    expect(groups[0].rows.map(r => r.symbol).sort()).toEqual(['EUR', 'UAH Cash', 'USD Cash']);
    expect(groups.find(g => g.assetClass === 'equity')?.rows.map(r => r.symbol)).toEqual(['AAPL']);
    expect(computed.allocationSegments().map(s => s.label)).toEqual([
      'Cash',
      'Bonds',
      'Real estate',
      'Equities',
    ]);
  });

  it('shows the provider display name and carries the P&L value, biggest holding first', () => {
    const computed = build([
      position({symbol: 'SMALL', provider: 'ibkr', currentValue: 100, pnlPercent: 10, pnlUsd: 10}),
      position({symbol: 'BIG', provider: 'ibkr', currentValue: 900, pnlPercent: -5, pnlUsd: -47}),
    ]);

    const rows = computed.positionsByAssetClass()[0].rows;

    expect(rows.map(r => r.symbol)).toEqual(['BIG', 'SMALL']);
    expect(rows[0]).toEqual(
      expect.objectContaining({providerLabel: 'Interactive Brokers', pnlUsd: -47})
    );
  });

  it('falls back to the raw slug for an unknown provider', () => {
    const computed = build([position({provider: 'mystery', currentValue: 1})]);

    expect(computed.positionsByAssetClass()[0].rows[0].providerLabel).toBe('mystery');
  });

  it('maps the day change onto stock rows and leaves rows without a quote empty', () => {
    const stock = {provider: 'ibkr', instrumentType: 'STK', assetClass: 'equity' as const};
    const computed = build(
      [
        position({...stock, symbol: 'AAPL', currentValue: 110, currentPrice: 110}),
        position({...stock, symbol: 'MSFT', currentValue: 50, currentPrice: 50}),
        position({symbol: 'BTC', provider: 'binance', currentValue: 60}),
        position({
          symbol: 'EUR',
          provider: 'revolut_x',
          currentValue: 20,
          isVenueCash: true,
          assetClass: 'cash',
        }),
        position({
          symbol: 'USD Cash',
          provider: 'ibkr',
          instrumentType: 'CASH',
          currentValue: 5,
          assetClass: 'cash',
        }),
      ],
      {AAPL: 10, BTC: 5, 'USD CASH': 1}
    );

    const rows = computed.positionsByAssetClass().flatMap(g => g.rows);
    const bySymbol = (s: string) => rows.find(r => r.symbol === s);

    expect(bySymbol('AAPL')?.dayChangePct).toBe(10);
    expect(bySymbol('AAPL')?.dayChangeUsd).toBeCloseTo(10);
    expect(bySymbol('MSFT')).toEqual(
      expect.objectContaining({dayChangePct: null, dayChangeUsd: null})
    );
    expect(bySymbol('BTC')?.dayChangePct).toBeNull();
    expect(bySymbol('EUR')?.dayChangePct).toBeNull();
    expect(bySymbol('USD Cash')?.dayChangePct).toBeNull();
  });
  it('derives a native unit price only for a row that carries its own currency', () => {
    const computed = build([
      position({
        symbol: 'Fund A',
        provider: 'inzhur',
        quantity: 10,
        currentValue: 30,
        nativeCurrency: 'UAH',
        nativeValue: 1_200,
        assetClass: 'realEstate',
      }),
      position({symbol: 'AAPL', provider: 'ibkr', currentValue: 10, assetClass: 'equity'}),
    ]);

    const rows = computed.positionsByAssetClass().flatMap(g => g.rows);
    const bySymbol = (s: string) => rows.find(r => r.symbol === s);

    expect(bySymbol('Fund A')).toEqual(
      expect.objectContaining({nativeCurrency: 'UAH', nativeValue: 1_200, nativePrice: 120})
    );
    expect(bySymbol('AAPL')).toEqual(
      expect.objectContaining({nativeCurrency: null, nativeValue: null, nativePrice: null})
    );
  });
});

describe('holdingsComputed provider filter', () => {
  const ibkr = position({
    symbol: 'AAPL',
    provider: 'ibkr',
    assetClass: 'equity',
    currentValue: 300,
  });
  const binance = position({symbol: 'ETH', provider: 'binance', currentValue: 100});

  it('keeps every position when no provider is selected', () => {
    const c = build([ibkr, binance]);

    expect(c.positions()).toHaveLength(2);
    expect(c.providerFilterLabel()).toBeNull();
    expect(c.totalPositionsValue()).toBe(400);
  });

  it('narrows positions, total, groups and weights to the selected provider', () => {
    const c = build([ibkr, binance], {}, 'success', null, 'ibkr');

    expect(c.positions().map(p => p.symbol)).toEqual(['AAPL']);
    expect(c.totalPositionsValue()).toBe(300);
    expect(c.positionsByAssetClass().map(g => g.assetClass)).toEqual(['equity']);
    expect(c.positionsByAssetClass()[0].rows[0].weightPercent).toBe(100);
    expect(c.providerFilterLabel()).toBe('Interactive Brokers');
    expect(c.isFilteredEmpty()).toBe(false);
  });

  it('reports a filter that matches nothing without reading as an empty book', () => {
    const c = build([ibkr], {}, 'success', null, 'binance');

    expect(c.positions()).toHaveLength(0);
    expect(c.isFilteredEmpty()).toBe(true);
    expect(c.isEmpty()).toBe(false);
  });

  const staleFeed = (source: string): Headline => ({
    amount: 300,
    percent: null,
    method: 'VALUE',
    scope: 'umbrella',
    window: {to: '2026-10-15'},
    quality: 'DEGRADED',
    reasons: [{code: 'STALE_SOURCE', source}],
    currency: 'USD',
  });

  it('flags the filtered provider when its feed failed, never an empty provider', () => {
    const c = build([ibkr], {}, 'success', staleFeed('crypto'), 'binance');

    expect(c.isProviderFeedFailed()).toBe(true);
    expect(c.isFilteredEmpty()).toBe(false);
    expect(c.isTotalUnavailable()).toBe(true);
  });

  it('maps a broker provider to the brokerage feed', () => {
    const c = build([binance], {}, 'success', staleFeed('brokerage'), 'ibkr');

    expect(c.isProviderFeedFailed()).toBe(true);
    expect(c.isFilteredEmpty()).toBe(false);
  });

  it('keeps the filtered figure when only the other feed failed', () => {
    const c = build([ibkr], {}, 'success', staleFeed('crypto'), 'ibkr');

    expect(c.isProviderFeedFailed()).toBe(false);
    expect(c.isTotalUnavailable()).toBe(false);
    expect(c.totalPositionsValue()).toBe(300);
  });
});
