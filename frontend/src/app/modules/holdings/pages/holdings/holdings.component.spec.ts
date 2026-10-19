import {DecimalPipe} from '@angular/common';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {provideRouter, Router} from '@angular/router';
import {DonutChartComponent} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type Headline} from '../../../../shared/models/headline/headline.model';
import {ConnectStore} from '../../../bank-sync/store/connect/connect.store';
import {BrokerageGrowthComponent} from '../../components/brokerage-growth/brokerage-growth.component';
import {GrowthOfTenThousandComponent} from '../../components/growth-of-ten-thousand/growth-of-ten-thousand.component';
import {type Position} from '../../models/position/position.model';
import {BookPerformanceStore} from '../../store/book-performance/book-performance.store';
import {BrokerageGrowthStore} from '../../store/brokerage-growth/brokerage-growth.store';
import {type AllocationBreakdownRow, type PositionAssetGroup} from '../../store/holdings.computed';
import {HoldingsStore} from '../../store/holdings.store';
import {InvestmentsComponent} from './holdings.component';

const EQUITY_GROUP: PositionAssetGroup = {
  assetClass: 'equity',
  label: 'Equities',
  totalValue: 100,
  hiddenRows: [],
  hiddenTotal: 0,
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
      hiddenReason: null,
      weightPercent: 100,
    },
  ],
};

const CRYPTO_GROUP: PositionAssetGroup = {
  assetClass: 'crypto',
  label: 'Crypto',
  totalValue: 50,
  hiddenRows: [],
  hiddenTotal: 0,
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
      hiddenReason: null,
      weightPercent: 100,
    },
  ],
};

describe('InvestmentsComponent — positions view', () => {
  let mockStore: {
    isLoading: ReturnType<typeof signal<boolean>>;
    errorMessage: ReturnType<typeof signal<string>>;
    positions: ReturnType<typeof signal<Position[]>>;
    positionsByAssetClass: ReturnType<typeof signal<PositionAssetGroup[]>>;
    totalPositionsValue: ReturnType<typeof signal<number>>;
    totalHeadline: ReturnType<typeof signal<Nullable<Headline>>>;
    isTotalUnavailable: ReturnType<typeof signal<boolean>>;
    isEmpty: ReturnType<typeof signal<boolean>>;
    allocationSegments: ReturnType<typeof signal<never[]>>;
    allocationBreakdown: ReturnType<typeof signal<AllocationBreakdownRow[]>>;
    providerFilter: ReturnType<typeof signal<Nullable<string>>>;
    providerFilterLabel: ReturnType<typeof signal<Nullable<string>>>;
    isFilteredEmpty: ReturnType<typeof signal<boolean>>;
    isProviderFeedFailed: ReturnType<typeof signal<boolean>>;
  };

  let mockConnectStore: {importPending: ReturnType<typeof signal<boolean>>};

  beforeEach(async () => {
    mockConnectStore = {importPending: signal(false)};
    mockStore = {
      isLoading: signal(false),
      errorMessage: signal(''),
      positions: signal<Position[]>([
        {
          symbol: 'DRAM',
          provider: 'ibkr',
          instrumentType: 'STK',
          quantity: 10,
          currentValue: 100,
          currentPrice: 10,
          pnlPercent: 25.3,
          pnlUsd: 20.2,
          isVenueCash: false,
          assetClass: 'equity',
        },
        {
          symbol: 'SOL',
          provider: 'binance',
          instrumentType: null,
          quantity: 1,
          currentValue: 50,
          currentPrice: 50,
          pnlPercent: null,
          pnlUsd: null,
          isVenueCash: false,
          assetClass: 'crypto',
        },
      ]),
      positionsByAssetClass: signal<PositionAssetGroup[]>([EQUITY_GROUP, CRYPTO_GROUP]),
      totalPositionsValue: signal(150),
      totalHeadline: signal<Nullable<Headline>>(null),
      isTotalUnavailable: signal(false),
      isEmpty: signal(false),
      providerFilter: signal<Nullable<string>>(null),
      providerFilterLabel: signal<Nullable<string>>(null),
      isFilteredEmpty: signal(false),
      isProviderFeedFailed: signal(false),
      allocationSegments: signal([]),
      allocationBreakdown: signal<AllocationBreakdownRow[]>([
        {label: 'Equities', color: 'var(--color-asset-equity)', value: 100, percent: 67},
        {label: 'Crypto', color: 'var(--color-asset-crypto)', value: 50, percent: 33},
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
              useValue: mockConnectStore,
            },
          ],
        },
      })
      .overrideComponent(BrokerageGrowthComponent, {
        set: {
          providers: [
            {
              provide: BrokerageGrowthStore,
              useValue: {status: signal('success'), hasHistory: signal(false)},
            },
          ],
        },
      })
      .overrideComponent(GrowthOfTenThousandComponent, {
        set: {
          providers: [
            {
              provide: BookPerformanceStore,
              useValue: {status: signal('success'), growth: signal(null)},
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
    expect(up?.className).toContain('text-gain');
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
    expect(cell?.className).toContain('text-loss');
  });

  it('shows the provider as a subtitle under the symbol', () => {
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Interactive Brokers');
    expect(text).toContain('Binance');
  });

  it('renders skeleton rows while positions load', () => {
    mockStore.isLoading.set(true);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="holdings-skeleton"]')).not.toBeNull();
    expect(root.querySelector('cmn-data-table')).toBeNull();
  });

  it('replaces the content with the error alert when positions fail to load', () => {
    mockStore.errorMessage.set('Failed to load positions.');

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('cmn-alert')?.textContent).toContain('Failed to load positions.');
    expect(root.querySelector('cmn-data-table')).toBeNull();
    expect(root.querySelector('[data-testid="holdings-skeleton"]')).toBeNull();
  });

  it('hides the donut when there is nothing to draw', () => {
    mockStore.totalPositionsValue.set(0);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('cmn-donut-chart')).toBeNull();
  });

  it('shows an empty state when there are no positions', () => {
    mockStore.positionsByAssetClass.set([]);
    mockStore.isEmpty.set(true);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No positions yet');
  });

  it('says the import is pending instead of the connect prompt while IBKR positions import', () => {
    mockStore.positionsByAssetClass.set([]);
    mockStore.isEmpty.set(true);
    mockConnectStore.importPending.set(true);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="holdings-importing"]')).not.toBeNull();
    expect(root.textContent).not.toContain('No positions yet');
  });

  it('shows no filter chip while the list is unfiltered', () => {
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="holdings-filter-chip"]')
    ).toBeNull();
  });

  it('shows a removable chip for the provider filter and clears it from the URL', () => {
    mockStore.providerFilter.set('ibkr');
    mockStore.providerFilterLabel.set('Interactive Brokers');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const chip = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="holdings-filter-chip"]'
    );
    expect(chip?.getAttribute('label')).toBe('Interactive Brokers');

    chip?.dispatchEvent(new CustomEvent('lk-dismissible-chip-remove'));

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {provider: null},
      queryParamsHandling: 'merge',
    });
  });

  it('shows the brokerage growth section on the whole book only', () => {
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('fns-brokerage-growth')).not.toBeNull();

    mockStore.providerFilter.set('ibkr');
    fixture.detectChanges();

    expect(root.querySelector('fns-brokerage-growth')).toBeNull();
  });

  it('says so when the provider filter matches no positions', () => {
    mockStore.providerFilter.set('binance');
    mockStore.providerFilterLabel.set('Binance');
    mockStore.positionsByAssetClass.set([]);
    mockStore.isFilteredEmpty.set(true);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="holdings-filter-empty"]')?.textContent).toContain(
      'No positions with Binance'
    );
    expect(root.textContent).not.toContain('No positions yet');
  });

  it('says the provider could not be loaded when its feed failed, not that it has no positions', () => {
    mockStore.providerFilter.set('binance');
    mockStore.providerFilterLabel.set('Binance');
    mockStore.positionsByAssetClass.set([]);
    mockStore.isTotalUnavailable.set(true);
    mockStore.isProviderFeedFailed.set(true);

    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(
      root.querySelector('[data-testid="holdings-filter-feed-failed"]')?.textContent
    ).toContain('Binance positions could not be loaded');
    expect(root.querySelector('[data-testid="holdings-total-value"]')?.textContent).toContain(
      'N/A'
    );
    expect(root.querySelector('[data-testid="holdings-filter-empty"]')).toBeNull();
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

  it('renders no dossier link for a cash row', () => {
    mockStore.positionsByAssetClass.set([
      {
        assetClass: 'cash',
        label: 'Cash',
        totalValue: 300,
        hiddenRows: [],
        hiddenTotal: 0,
        rows: [
          {...EQUITY_GROUP.rows[0], symbol: 'USD Cash', currentPrice: null, dayChangePct: null},
        ],
      },
    ]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(InvestmentsComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('USD Cash');
    root.querySelector<HTMLElement>('[data-testid="list-row"], tbody tr')?.click();

    expect(navigate).not.toHaveBeenCalled();
  });

  describe('allocation donut click-through', () => {
    function clickSegment(index: number): ReturnType<typeof vi.fn> {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const fixture = TestBed.createComponent(InvestmentsComponent);
      fixture.detectChanges();
      const donut = fixture.debugElement.query(By.directive(DonutChartComponent))
        .componentInstance as DonutChartComponent;
      donut.segmentClick.emit({index, label: 'x', value: 1});
      return navigate;
    }

    it('opens the dossier of the only position in a clicked asset class', () => {
      expect(clickSegment(0)).toHaveBeenCalledWith(['/assets', 'DRAM']);
    });

    it('opens the crypto position of the clicked crypto slice', () => {
      expect(clickSegment(1)).toHaveBeenCalledWith(['/assets', 'SOL']);
    });

    it('opens nothing for a class holding several positions', () => {
      mockStore.positionsByAssetClass.set([
        {...EQUITY_GROUP, rows: [EQUITY_GROUP.rows[0], {...EQUITY_GROUP.rows[0], symbol: 'VOO'}]},
      ]);

      expect(clickSegment(0)).not.toHaveBeenCalled();
    });

    it('opens nothing for the cash slice', () => {
      mockStore.positionsByAssetClass.set([
        {
          assetClass: 'cash',
          label: 'Cash',
          totalValue: 300,
          hiddenRows: [],
          hiddenTotal: 0,
          rows: [EQUITY_GROUP.rows[0]],
        },
      ]);

      expect(clickSegment(0)).not.toHaveBeenCalled();
    });
  });

  describe('hidden positions', () => {
    const DUST = {
      ...EQUITY_GROUP.rows[0],
      symbol: 'DUST',
      currentValue: 0.5,
      hiddenReason: 'small' as const,
    };
    const WITH_HIDDEN: PositionAssetGroup = {
      ...EQUITY_GROUP,
      totalValue: 100.5,
      hiddenRows: [DUST],
      hiddenTotal: 0.5,
    };

    function render(groups: PositionAssetGroup[]): HTMLElement {
      mockStore.positionsByAssetClass.set(groups);
      const fixture = TestBed.createComponent(InvestmentsComponent);
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    }

    it('closes a group that has hidden rows with a collapsed "N hidden" row and keeps them out of the list', () => {
      const root = render([WITH_HIDDEN]);

      const hidden = root.querySelector('[data-testid="holdings-hidden"]');
      expect(hidden?.querySelector('summary')?.textContent).toContain('1 hidden · $0.50');
      expect(hidden?.querySelector('details')?.open).toBe(false);
      expect(
        root.querySelectorAll(
          '[data-testid="holdings-hidden-rows"] [data-testid="list-row"], [data-testid="holdings-hidden-rows"] tbody tr'
        ).length
      ).toBe(1);
      expect(root.textContent).toContain('DRAM');
    });

    it('shows the group total over every row, hidden included', () => {
      const root = render([WITH_HIDDEN]);

      expect(root.querySelector('[data-testid="holdings-group"]')?.textContent).toContain(
        '$100.50'
      );
    });

    it('adds no hidden row to a group with nothing hidden', () => {
      expect(render([EQUITY_GROUP]).querySelector('[data-testid="holdings-hidden"]')).toBeNull();
    });

    it('renders a group whose rows are all hidden as just the hidden row', () => {
      const root = render([{...WITH_HIDDEN, rows: []}]);

      expect(root.querySelector('[data-testid="holdings-hidden"]')).not.toBeNull();
      expect(root.querySelectorAll('[data-testid="holdings-group"] > cmn-data-table').length).toBe(
        0
      );
    });

    it('opens the dossier of a class whose only position is hidden when its slice is clicked', () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      mockStore.positionsByAssetClass.set([{...WITH_HIDDEN, rows: []}]);
      const fixture = TestBed.createComponent(InvestmentsComponent);
      fixture.detectChanges();
      const donut = fixture.debugElement.query(By.directive(DonutChartComponent))
        .componentInstance as DonutChartComponent;
      donut.segmentClick.emit({index: 0, label: 'x', value: 1});

      expect(navigate).toHaveBeenCalledWith(['/assets', 'DUST']);
    });
  });
});
