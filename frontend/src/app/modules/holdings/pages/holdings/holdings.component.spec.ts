import {DecimalPipe} from '@angular/common';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it} from 'vitest';

import {type Position} from '../../models/position/position.model';
import {type AllocationBreakdownRow, type PositionAssetGroup} from '../../store/holdings.computed';
import {HoldingsStore} from '../../store/holdings.store';
import {InvestmentsComponent} from './holdings.component';

const EQUITY_GROUP: PositionAssetGroup = {
  assetClass: 'equity',
  label: 'Equities',
  totalValue: 100,
  rows: [
    {
      symbol: 'DRAM',
      provider: 'ibkr',
      quantity: 10,
      currentPrice: 10,
      currentValue: 100,
      pnlPercent: 25.3,
      weightPercent: 100,
    },
  ],
};

const CRYPTO_GROUP: PositionAssetGroup = {
  assetClass: 'crypto',
  label: 'Crypto',
  totalValue: 50,
  rows: [
    {
      symbol: 'SOL',
      provider: 'binance',
      quantity: 1,
      currentPrice: 50,
      currentValue: 50,
      pnlPercent: null,
      weightPercent: 100,
    },
  ],
};

describe('InvestmentsComponent — positions view', () => {
  let mockStore: {
    isPositionsLoading: ReturnType<typeof signal<boolean>>;
    positionsErrorMessage: ReturnType<typeof signal<string>>;
    positions: ReturnType<typeof signal<Position[]>>;
    positionsByAssetClass: ReturnType<typeof signal<PositionAssetGroup[]>>;
    totalPositionsValue: ReturnType<typeof signal<number>>;
    allocationSegments: ReturnType<typeof signal<never[]>>;
    allocationBreakdown: ReturnType<typeof signal<AllocationBreakdownRow[]>>;
  };

  beforeEach(async () => {
    mockStore = {
      isPositionsLoading: signal(false),
      positionsErrorMessage: signal(''),
      positions: signal<Position[]>([
        {
          symbol: 'DRAM',
          provider: 'ibkr',
          quantity: 10,
          currentValue: 100,
          currentPrice: 10,
          pnlPercent: 25.3,
          isVenueCash: false,
        },
        {
          symbol: 'SOL',
          provider: 'binance',
          quantity: 1,
          currentValue: 50,
          currentPrice: 50,
          pnlPercent: null,
          isVenueCash: false,
        },
      ]),
      positionsByAssetClass: signal<PositionAssetGroup[]>([EQUITY_GROUP, CRYPTO_GROUP]),
      totalPositionsValue: signal(150),
      allocationSegments: signal([]),
      allocationBreakdown: signal<AllocationBreakdownRow[]>([
        {label: 'Equities', color: '#6366f1', value: 100, percent: 67},
        {label: 'Crypto', color: '#f59e0b', value: 50, percent: 33},
      ]),
    };

    await TestBed.configureTestingModule({
      imports: [InvestmentsComponent],
      providers: [DecimalPipe],
    })
      .overrideComponent(InvestmentsComponent, {
        set: {providers: [{provide: HoldingsStore, useValue: mockStore}]},
      })
      .compileComponents();
  });

  it('renders a group card per asset class', () => {
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Equities');
    expect(text).toContain('Crypto');
    expect(text).toContain('DRAM');
    expect(text).toContain('SOL');
  });

  it('renders provider-reported P&L for equities and an em dash when P&L is unavailable', () => {
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('25.30%');
    expect(text).toContain('—');
  });

  it('shows an empty state when there are no positions', () => {
    mockStore.positionsByAssetClass.set([]);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No positions yet');
  });
});
