import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {describe, expect, it} from 'vitest';

import {type Position} from '../models/position/position.model';
import {holdingsComputed} from './holdings.computed';
import {type HoldingsState} from './holdings.state';

function position(overrides: Partial<Position>): Position {
  return {
    symbol: 'BTC',
    provider: 'revolut_x',
    quantity: 1,
    currentValue: 0,
    currentPrice: 0,
    pnlPercent: null,
    isVenueCash: false,
    ...overrides,
  };
}

function build(positions: Position[]) {
  TestBed.configureTestingModule({providers: [{provide: ERROR_MESSAGES, useValue: {}}]});
  return TestBed.runInInjectionContext(() =>
    holdingsComputed({
      positions: signal(positions),
      positionsStatus: signal<HoldingsState['positionsStatus']>('idle'),
      positionsErrorCode: signal<Nullable<string>>(null),
    })
  );
}

describe('holdingsComputed', () => {
  it('groups venue fiat as venue cash, apart from the crypto held on the same venue', () => {
    const computed = build([
      position({symbol: 'BTC', currentValue: 6_000, currentPrice: 60_000, quantity: 0.1}),
      position({
        symbol: 'EUR',
        currentValue: 2_000,
        currentPrice: null,
        quantity: 1_850,
        isVenueCash: true,
      }),
      position({symbol: 'AAPL', provider: 'ibkr', currentValue: 2_000, currentPrice: 200}),
    ]);

    const groups = computed.positionsByAssetClass();

    expect(groups.map(g => [g.assetClass, g.label, g.totalValue])).toEqual([
      ['equity', 'Equities', 2_000],
      ['crypto', 'Crypto', 6_000],
      ['venueCash', 'Venue cash', 2_000],
    ]);
    expect(groups[1].rows.map(r => r.symbol)).toEqual(['BTC']);
    expect(groups[2].rows).toEqual([
      expect.objectContaining({
        symbol: 'EUR',
        provider: 'revolut_x',
        currentPrice: null,
        weightPercent: 20,
      }),
    ]);
    expect(computed.allocationBreakdown().map(r => [r.label, r.percent])).toEqual([
      ['Equities', 20],
      ['Crypto', 60],
      ['Venue cash', 20],
    ]);
  });
});
