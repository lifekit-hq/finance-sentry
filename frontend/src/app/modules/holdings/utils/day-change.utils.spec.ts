import {describe, expect, it} from 'vitest';

import {type Position} from '../models/position/position.model';
import {type QuoteDto} from '../models/quote/quote.model';
import {DayChangeUtils} from './day-change.utils';

function position(overrides: Partial<Position>): Position {
  return {
    symbol: 'AAPL',
    provider: 'ibkr',
    instrumentType: 'STK',
    quantity: 1,
    currentValue: 100,
    currentPrice: 100,
    pnlPercent: null,
    pnlUsd: null,
    isVenueCash: false,
    ...overrides,
  };
}

function quote(overrides: Partial<QuoteDto>): QuoteDto {
  return {
    ticker: 'AAPL',
    resolvedTicker: null,
    price: 100,
    previousClose: 99,
    changePct: 1,
    currency: 'USD',
    ...overrides,
  };
}

describe('DayChangeUtils', () => {
  describe('quotableTickers', () => {
    it('keeps stocks, upper-cased and de-duplicated across brokers', () => {
      const tickers = DayChangeUtils.quotableTickers([
        position({symbol: 'aapl'}),
        position({symbol: 'AAPL', provider: 'other', instrumentType: 'stk'}),
      ]);
      expect(tickers).toEqual(['AAPL']);
    });

    it('skips crypto, venue cash and non-stock brokerage rows', () => {
      const tickers = DayChangeUtils.quotableTickers([
        position({symbol: 'BTC', provider: 'binance', instrumentType: null}),
        position({symbol: 'EUR', provider: 'revolut_x', instrumentType: null, isVenueCash: true}),
        position({symbol: 'USD Cash', instrumentType: 'CASH'}),
        position({symbol: 'UA4000227045', instrumentType: 'BOND'}),
        position({symbol: 'Fund A', provider: 'inzhur', instrumentType: 'REIT'}),
      ]);
      expect(tickers).toEqual([]);
    });
  });

  describe('pctByTicker', () => {
    it('maps quotes by upper-cased ticker', () => {
      expect(DayChangeUtils.pctByTicker([quote({ticker: 'aapl', changePct: -2.5})])).toEqual({
        AAPL: -2.5,
      });
    });

    it('leaves out quotes without a change', () => {
      expect(DayChangeUtils.pctByTicker([quote({changePct: null})])).toEqual({});
    });
  });

  describe('dayChangeUsd', () => {
    it('derives the gain from the current value and the percent', () => {
      expect(DayChangeUtils.dayChangeUsd(110, 10)).toBeCloseTo(10);
      expect(DayChangeUtils.dayChangeUsd(90, -10)).toBeCloseTo(-10);
    });

    it('is null without a change', () => {
      expect(DayChangeUtils.dayChangeUsd(100, null)).toBeNull();
    });

    it('is null for a degenerate -100% change', () => {
      expect(DayChangeUtils.dayChangeUsd(0, -100)).toBeNull();
    });
  });
});
