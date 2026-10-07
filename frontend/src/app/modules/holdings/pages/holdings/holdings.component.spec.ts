import {DecimalPipe} from '@angular/common';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ConnectStore} from '../../../bank-sync/store/connect/connect.store';
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
      providerLabel: 'Interactive Brokers',
      quantity: 10,
      currentPrice: 10,
      currentValue: 100,
      pnlPercent: 25.3,
      pnlUsd: 20.2,
      dayChangePct: 1.25,
      dayChangeUsd: 1.23,
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
      providerLabel: 'Binance',
      quantity: 1,
      currentPrice: 50,
      currentValue: 50,
      pnlPercent: null,
      pnlUsd: null,
      dayChangePct: null,
      dayChangeUsd: null,
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
          pnlUsd: 20.2,
          isVenueCash: false,
        },
        {
          symbol: 'SOL',
          provider: 'binance',
          quantity: 1,
          currentValue: 50,
          currentPrice: 50,
          pnlPercent: null,
          pnlUsd: null,
          isVenueCash: false,
        },
      ]),
      positionsByAssetClass: signal<PositionAssetGroup[]>([EQUITY_GROUP, CRYPTO_GROUP]),
      totalPositionsValue: signal(150),
      allocationSegments: signal([]),
      allocationBreakdown: signal<AllocationBreakdownRow[]>([
        {label: 'Equities', color: 'var(--color-chart-series-1)', value: 100, percent: 67},
        {label: 'Crypto', color: 'var(--color-chart-series-2)', value: 50, percent: 33},
      ]),
    };

    await TestBed.configureTestingModule({
      imports: [InvestmentsComponent],
      providers: [DecimalPipe, provideRouter([])],
    })
      .overrideComponent(InvestmentsComponent, {
        set: {
          providers: [
            {provide: HoldingsStore, useValue: mockStore},
            {
              provide: ConnectStore,
              useValue: {importPending: signal(false)},
            },
          ],
        },
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
    expect(text).toContain('+$20.20');
    expect(text).toContain('25.30%');
    expect(text).toContain('—');
  });

  it('renders the day change coloured by sign, with a placeholder for a row without a quote', () => {
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const cells = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        '[data-testid="holding-day-change"]'
      )
    );
    const up = cells.find(c => c.textContent?.includes('+1.25%'));
    const none = cells.find(c => c.textContent?.trim() === '—');
    expect(up?.className).toContain('text-status-success');
    expect(none?.className).toContain('text-text-secondary');
  });

  it('colours a negative day change as a loss', () => {
    mockStore.positionsByAssetClass.set([
      {...EQUITY_GROUP, rows: [{...EQUITY_GROUP.rows[0], dayChangePct: -0.8, dayChangeUsd: -0.8}]},
    ]);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const cell = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="holding-day-change"]'
    );
    expect(cell?.textContent).toContain('-0.80%');
    expect(cell?.className).toContain('text-status-error');
  });

  it('shows the provider as a subtitle under the symbol', () => {
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Interactive Brokers');
    expect(text).toContain('Binance');
  });

  it('renders skeleton rows while positions load', () => {
    mockStore.isPositionsLoading.set(true);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="holdings-skeleton"]')).not.toBeNull();
    expect(root.querySelector('cmn-data-table')).toBeNull();
  });

  it('hides the donut when there is nothing to draw', () => {
    mockStore.totalPositionsValue.set(0);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('cmn-donut-chart')).toBeNull();
  });

  it('shows an empty state when there are no positions', () => {
    mockStore.positionsByAssetClass.set([]);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No positions yet');
  });

  it('opens the asset page when a holding row is clicked', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const row = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="list-row"], tbody tr'
    );
    row?.click();

    expect(navigate).toHaveBeenCalledWith(['/assets', 'DRAM']);
  });

  it('links the provider label to the investment accounts list without opening the asset', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      '[data-testid="holding-provider-link"]'
    );
    expect(link?.getAttribute('href')).toBe('/accounts/investments');
    link?.addEventListener('click', e => e.preventDefault());
    link?.click();
    expect(navigate).not.toHaveBeenCalled();
  });
});
