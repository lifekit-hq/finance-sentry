import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type Position} from '../models/position/position.model';
import {PositionsService} from '../services/positions.service';
import {QuotesService} from '../services/quotes.service';
import {holdingsEffects} from './holdings.effects';

const FAKE_POSITIONS: Position[] = [];
const EQUITY: Position = {
  symbol: 'AAPL',
  provider: 'ibkr',
  instrumentType: 'STK',
  quantity: 1,
  currentValue: 110,
  currentPrice: 110,
  pnlPercent: null,
  pnlUsd: null,
  isVenueCash: false,
  assetClass: 'equity',
};

function buildStore() {
  return {
    setPositionsLoading: vi.fn(),
    setPositions: vi.fn(),
    setPositionsError: vi.fn(),
    setDayChangePct: vi.fn(),
  };
}

function buildPositions() {
  return {getPositions: vi.fn().mockReturnValue(of(FAKE_POSITIONS))};
}

function configure(
  positions: ReturnType<typeof buildPositions>,
  quotes: {getQuotes: ReturnType<typeof vi.fn>} = {getQuotes: vi.fn().mockReturnValue(of([]))}
): void {
  TestBed.configureTestingModule({
    providers: [
      {provide: PositionsService, useValue: positions},
      {provide: QuotesService, useValue: quotes},
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

    expect(store.setPositionsLoading).toHaveBeenCalledOnce();
    expect(store.setPositions).toHaveBeenCalledWith(FAKE_POSITIONS);
  });

  it('loadPositions: error path forwards errorCode', () => {
    const store = buildStore();
    const positions = buildPositions();
    positions.getPositions.mockReturnValue(
      throwError(() => ({error: {errorCode: 'POSITIONS_FETCH_FAILED'}}))
    );
    configure(positions);

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(store.setPositionsError).toHaveBeenCalledWith('POSITIONS_FETCH_FAILED');
  });

  it('loadPositions: quotes the held equities once and stores their day change', () => {
    const store = buildStore();
    const positions = buildPositions();
    positions.getPositions.mockReturnValue(
      of([
        EQUITY,
        {...EQUITY, provider: 'binance', symbol: 'BTC', instrumentType: null},
        {...EQUITY, symbol: 'USD Cash', instrumentType: 'CASH'},
      ])
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
    positions.getPositions.mockReturnValue(of([EQUITY]));
    configure(positions, {getQuotes: vi.fn().mockReturnValue(throwError(() => new Error('boom')))});

    TestBed.runInInjectionContext(() => {
      holdingsEffects(store).loadPositions();
    });

    expect(store.setPositions).toHaveBeenCalledWith([EQUITY]);
    expect(store.setPositionsError).not.toHaveBeenCalled();
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
});
