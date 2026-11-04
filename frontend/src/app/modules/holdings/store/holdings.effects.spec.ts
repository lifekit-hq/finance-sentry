import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../auth/store/auth.store';
import {type Position} from '../models/position/position.model';
import {PositionsService} from '../services/positions.service';
import {QuotesService} from '../services/quotes.service';
import {VerdictsService} from '../services/verdicts.service';
import {holdingsEffects} from './holdings.effects';

const FAKE_POSITIONS: Position[] = [];
const EQUITY: Position = {
  symbol: 'AAPL',
  provider: 'ibkr',
  instrumentType: 'STK',
  quantity: 1,
  currentValue: 110,
  currentPrice: 110,
  averagePrice: null,
  pnlPercent: null,
  pnlUsd: null,
  isVenueCash: false,
  assetClass: 'equity',
};

function buildStore() {
  return {
    setLoading: vi.fn(),
    setSuccess: vi.fn(),
    setPositions: vi.fn(),
    setHeadline: vi.fn(),
    setError: vi.fn(),
    setDayChangePct: vi.fn(),
    setVerdicts: vi.fn(),
  };
}

function buildPositions() {
  return {getHoldings: vi.fn().mockReturnValue(of({positions: FAKE_POSITIONS, headline: null}))};
}

function configure(
  positions: ReturnType<typeof buildPositions>,
  quotes: {getQuotes: ReturnType<typeof vi.fn>} = {getQuotes: vi.fn().mockReturnValue(of([]))},
  verdicts: {getVerdicts: ReturnType<typeof vi.fn>} = {
    getVerdicts: vi.fn().mockReturnValue(of([])),
  },
  canUseAi = false
): void {
  TestBed.configureTestingModule({
    providers: [
      {provide: PositionsService, useValue: positions},
      {provide: QuotesService, useValue: quotes},
      {provide: VerdictsService, useValue: verdicts},
      {provide: AuthStore, useValue: {canUseAi: () => canUseAi}},
    ],
  });
}

describe('holdingsEffects', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('loadPositions: success path patches positions', () => {
    const store = buildStore();
    const positions = buildPositions();
    configure(positions);

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(store.setLoading).toHaveBeenCalledOnce();
    expect(store.setPositions).toHaveBeenCalledWith(FAKE_POSITIONS);
    expect(store.setSuccess).toHaveBeenCalledOnce();
  });

  it('loadPositions: stores the total headline beside the positions', () => {
    const store = buildStore();
    const positions = buildPositions();
    const headline = {quality: 'DEGRADED', reasons: [{code: 'STALE_SOURCE', source: 'crypto'}]};
    positions.getHoldings.mockReturnValue(of({positions: FAKE_POSITIONS, headline}));
    configure(positions);

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(store.setHeadline).toHaveBeenCalledWith(headline);
  });

  it('loadPositions: error path forwards errorCode', () => {
    const store = buildStore();
    const positions = buildPositions();
    positions.getHoldings.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 400, error: {errorCode: 'POSITIONS_FETCH_FAILED'}})
      )
    );
    configure(positions);

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(store.setError).toHaveBeenCalledWith('POSITIONS_FETCH_FAILED');
  });

  it('loadPositions: quotes the held equities once and stores their day change', () => {
    const store = buildStore();
    const positions = buildPositions();
    positions.getHoldings.mockReturnValue(
      of({
        positions: [
          EQUITY,
          {...EQUITY, provider: 'binance', symbol: 'BTC', instrumentType: null},
          {...EQUITY, symbol: 'USD Cash', instrumentType: 'CASH'},
        ],
        headline: null,
      })
    );
    const quotes = {
      getQuotes: vi.fn().mockReturnValue(
        of([
          {
            ticker: 'AAPL',
            resolvedTicker: null,
            price: 110,
            previousClose: 100,
            changePct: 10,
            currency: 'USD',
          },
        ])
      ),
    };
    configure(positions, quotes);

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(quotes.getQuotes).toHaveBeenCalledExactlyOnceWith(['AAPL']);
    expect(store.setDayChangePct).toHaveBeenCalledWith({AAPL: 10});
  });

  it('loadPositions: a failed quote fetch keeps the positions and the previous day changes', () => {
    const store = buildStore();
    const positions = buildPositions();
    positions.getHoldings.mockReturnValue(of({positions: [EQUITY], headline: null}));
    configure(positions, {getQuotes: vi.fn().mockReturnValue(throwError(() => new Error('boom')))});

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(store.setPositions).toHaveBeenCalledWith([EQUITY]);
    expect(store.setError).not.toHaveBeenCalled();
    expect(store.setDayChangePct).not.toHaveBeenCalled();
  });

  it('loadPositions: skips the quote call when nothing is quotable', () => {
    const store = buildStore();
    const positions = buildPositions();
    const quotes = {getQuotes: vi.fn()};
    configure(positions, quotes);

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(quotes.getQuotes).not.toHaveBeenCalled();
  });

  describe('verdict chips', () => {
    const owned = (getVerdicts: ReturnType<typeof vi.fn>, canUseAi = true) => {
      const store = buildStore();
      const positions = buildPositions();
      positions.getHoldings.mockReturnValue(
        of({
          positions: [
            EQUITY,
            {...EQUITY, provider: 'binance', symbol: 'BTC', instrumentType: null},
          ],
          headline: null,
        })
      );
      configure(positions, undefined, {getVerdicts}, canUseAi);
      TestBed.runInInjectionContext(() => {
        holdingsEffects(store).loadPositions();
      });
      return store;
    };

    it('reads the held listed stocks in one batch and stores each word by ticker', () => {
      const getVerdicts = vi
        .fn()
        .mockReturnValue(of([{symbol: 'aapl', word: 'Buy', firedRuleId: 'buy'}]));

      const store = owned(getVerdicts);

      expect(getVerdicts).toHaveBeenCalledExactlyOnceWith(['AAPL']);
      expect(store.setVerdicts).toHaveBeenCalledWith({AAPL: 'Buy'});
    });

    it('never reads a verdict without ai.use', () => {
      const getVerdicts = vi.fn().mockReturnValue(of([]));

      const store = owned(getVerdicts, false);

      expect(getVerdicts).not.toHaveBeenCalled();
      expect(store.setVerdicts).not.toHaveBeenCalled();
    });

    it('keeps the positions and shows no chip when the batch read fails', () => {
      const getVerdicts = vi.fn().mockReturnValue(throwError(() => new Error('403')));

      const store = owned(getVerdicts);

      expect(store.setPositions).toHaveBeenCalled();
      expect(store.setError).not.toHaveBeenCalled();
      expect(store.setVerdicts).not.toHaveBeenCalled();
    });

    it('skips the call when nothing is a listed stock', () => {
      const getVerdicts = vi.fn();
      const store = buildStore();
      const positions = buildPositions();
      configure(positions, undefined, {getVerdicts}, true);

      TestBed.runInInjectionContext(() => {
        holdingsEffects(store).loadPositions();
      });

      expect(getVerdicts).not.toHaveBeenCalled();
    });
  });
});
