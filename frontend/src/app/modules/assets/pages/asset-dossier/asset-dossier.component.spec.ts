import {computed, signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {provideRouter} from '@angular/router';
import {type AsyncStatus} from '@lifekit-hq/core';
import {TagComponent} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type ChartRangeId,
  type DossierAnalystsSection,
  type DossierCheckAxisKey,
  type DossierPositionSection,
  type DossierQuoteDto,
  type DossierQuoteHeader,
  type MetricValue,
  type ThesisDto,
  type ValuationSnapshotDto,
} from '../../models/dossier/dossier.model';
import {dossierComputed} from '../../store/dossier.computed';
import {DossierStore} from '../../store/dossier.store';
import {DossierViewUtils} from '../../utils/dossier-view.utils';
import {AssetDossierComponent} from './asset-dossier.component';

const THESIS: ThesisDto = {
  id: 't1',
  ticker: 'DRAM',
  thesisText: 'Memory cycle is turning.',
  keyDataPoints: [],
  catalysts: [],
  invalidationTriggers: [
    {
      metric: 'revenue_yoy',
      direction: 'lessThan',
      threshold: 0.3,
      proxyTicker: null,
      consecutivePeriods: 2,
      periodType: 'quarterly',
    },
  ],
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  brokenAt: null,
  brokenReason: null,
  entryPrice: null,
};

const POSITION: DossierPositionSection = {
  provider: 'ibkr',
  assetClass: 'Equities',
  quantity: 10,
  currentValueUsd: 431.94,
  costBasisUsd: 400,
  unrealizedPnlUsd: 31.94,
  unrealizedPnlPercent: 7.985,
  shareOfBookPercent: null,
  taxLots: [
    {
      quantity: 10,
      currentValueUsd: 431.94,
      averageCostUsd: 40,
      costBasisUsd: 400,
      unrealizedPnlUsd: 31.94,
      unrealizedPnlPercent: 7.985,
      acquiredAt: '2025-01-01T00:00:00Z',
      isLongTerm: true,
    },
  ],
};

const DOSSIER_ANALYSTS: DossierAnalystsSection = {
  recentActions: [
    {
      ticker: 'DRAM',
      firm: 'Needham',
      actionType: 'upgrade',
      priorRating: null,
      newRating: 'Buy',
      priorTarget: null,
      newTarget: 500,
      actionDate: '2026-09-01',
      source: 'finnhub',
      sourceUrl: 'https://example.com/needham',
      ingestedAt: '2026-09-01T00:00:00Z',
    },
    {
      ticker: 'DRAM',
      firm: 'Barclays',
      actionType: 'reiterate',
      priorRating: null,
      newRating: null,
      priorTarget: null,
      newTarget: null,
      actionDate: '2026-09-02',
      source: 'finnhub',
      sourceUrl: null,
      ingestedAt: '2026-09-02T00:00:00Z',
    },
  ],
  coverage: 'inUniverse',
  trends: [
    {
      period: '2026-08-01',
      strongBuy: 5,
      buy: 12,
      hold: 8,
      sell: 1,
      strongSell: 0,
      source: 'x',
      ingestedAt: '2026-09-01T00:00:00Z',
    },
  ],
};

const NO_METRIC: MetricValue = {
  value: null,
  fiveYearAvg: null,
  historyWindowYears: null,
  historyUnavailable: true,
};

const STALE_VALUATION: ValuationSnapshotDto = {
  ticker: 'DRAM',
  notApplicable: false,
  price: 43,
  isStale: true,
  metrics: {
    trailingPe: NO_METRIC,
    forwardPe: NO_METRIC,
    evToEbitda: NO_METRIC,
    dividendYield: NO_METRIC,
  },
  consensusTarget: null,
  impliedUpsidePct: null,
  peerSet: null,
  sources: [],
  retrievedAt: '2026-09-01T08:00:00Z',
  name: null,
  sector: null,
  industry: null,
  priceAsOf: null,
};

const DOSSIER: AssetDossierDto = {
  symbol: 'DRAM',
  position: POSITION,
  thesis: THESIS,
  valuation: null,
  analysts: DOSSIER_ANALYSTS,
  recentNews: [
    {
      id: 'n1',
      source: 'Wire',
      title: 'Memory prices climb',
      url: 'https://example.com/a',
      summary: 'A long summary that should not render.',
      tickers: ['DRAM'],
      categories: [],
      publishedAt: new Date(Date.now() - 3 * 24 * 60 * 60 * 1000).toISOString(),
    },
  ],
  nextEarnings: null,
  radarSignals: [],
  generatedAt: new Date().toISOString(),
};

function buildMockStore() {
  const dossier = signal<AssetDossierDto | null>(DOSSIER);
  const chart = signal<AssetChartDto | null>(null);
  const chartStatus = signal<AsyncStatus>('success');
  const chartErrorMessage = signal('');
  const targetDraft = signal('');
  const quote = signal<DossierQuoteDto | null>(null);
  const derived = dossierComputed({
    dossier,
    ledgerRead: signal(null),
    isThesisExpanded: signal(false),
    quote,
    chart,
    chartStatus,
    chartErrorMessage,
    targetDraft,
  });
  return {
    isDossierLoading: signal(false),
    dossierErrorMessage: signal(''),
    dossier,
    hasDossierSections: signal(true),
    heroProfile: computed(() => {
      const current = dossier();
      return current ? DossierViewUtils.heroProfile(current) : null;
    }),
    sectionChips: computed(() => DossierViewUtils.sectionChips(dossier())),
    recommendationSummary: computed(() =>
      DossierViewUtils.recommendationSummary(dossier()?.analysts?.trends ?? [])
    ),
    ledgerRead: signal<{generatedAt: string | null} | null>(null),
    isLedgerReadLoading: signal(false),
    isLedgerReadStale: signal(false),
    ledgerReadNarrative: signal(''),
    ledgerReadErrorMessage: signal(''),
    visibleThesisParagraphs: signal(['Memory cycle is turning.']),
    thesisHasMore: signal(false),
    isThesisExpanded: signal(false),
    quoteHeader: signal<DossierQuoteHeader | null>(null),
    isQuoteLoading: signal(false),
    chart,
    chartStatus,
    chartErrorMessage,
    chartRange: signal<ChartRangeId>('1Y'),
    targetDraft,
    isEditingTarget: signal(false),
    isTargetLoading: signal(false),
    targetErrorMessage: signal(''),
    isCrypto: derived.isCrypto,
    performanceStrip: derived.performanceStrip,
    thesisHook: derived.thesisHook,
    sizeCap: derived.sizeCap,
    returnsView: derived.returnsView,
    wheel: derived.wheel,
    checkAxes: derived.checkAxes,
    checksPassed: derived.checksPassed,
    checksTotal: derived.checksTotal,
    sectionChecks: derived.sectionChecks,
    chartView: derived.chartView,
    chartAsyncStatus: derived.chartAsyncStatus,
    targetBand: derived.targetBand,
    parsedTargetDraft: derived.parsedTargetDraft,
    fundamentals: derived.fundamentals,
    dataStatus: derived.dataStatus,
    toggleThesisExpanded: vi.fn(),
    generateLedgerRead: vi.fn(),
    loadChart: vi.fn(),
    selectChartRange: vi.fn(),
    openTargetEditor: vi.fn(),
    closeTargetEditor: vi.fn(),
    setTargetDraft: vi.fn(),
    saveTarget: vi.fn(),
    clearTarget: vi.fn(),
  };
}

describe('AssetDossierComponent', () => {
  let fixture: ComponentFixture<AssetDossierComponent>;
  let canUseAi: ReturnType<typeof signal<boolean>>;
  let mockStore: ReturnType<typeof buildMockStore>;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const byTestId = (id: string): HTMLElement | null =>
    root().querySelector<HTMLElement>(`[data-testid="${id}"]`);

  beforeEach(async () => {
    canUseAi = signal(true);
    mockStore = buildMockStore();

    await TestBed.configureTestingModule({
      imports: [AssetDossierComponent],
      providers: [provideRouter([]), {provide: AuthStore, useValue: {canUseAi}}],
    })
      .overrideComponent(AssetDossierComponent, {
        set: {providers: [{provide: DossierStore, useValue: mockStore}]},
      })
      .compileComponents();

    fixture = TestBed.createComponent(AssetDossierComponent);
  });

  it('renders skeletons while loading', () => {
    mockStore.isDossierLoading.set(true);
    fixture.detectChanges();

    expect(byTestId('dossier-skeleton')).not.toBeNull();
  });

  describe('header quote', () => {
    it('shows price and a coloured day change beside the symbol', () => {
      mockStore.quoteHeader.set({priceText: '$189.30', changeText: '+1.23%', direction: 'up'});
      fixture.detectChanges();

      expect(byTestId('dossier-price')?.textContent).toContain('$189.30');
      const change = byTestId('dossier-day-change');
      expect(change?.textContent).toContain('+1.23%');
      expect(change?.className).toContain('text-gain');
      expect(byTestId('dossier-price')?.className).not.toContain('text-status');
    });

    it('colours a loss as an error delta', () => {
      mockStore.quoteHeader.set({priceText: '$189.30', changeText: '-0.50%', direction: 'down'});
      fixture.detectChanges();

      expect(byTestId('dossier-day-change')?.className).toContain('text-loss');
    });

    it('keeps an empty reserved slot when the quote is missing', () => {
      fixture.detectChanges();

      expect(byTestId('dossier-quote')).not.toBeNull();
      expect(byTestId('dossier-price')).toBeNull();
      expect(byTestId('dossier-day-change')).toBeNull();
      expect(byTestId('dossier-quote')?.className).toContain('min-h-');
    });

    it('shows a skeleton inside the slot while the quote loads', () => {
      mockStore.isQuoteLoading.set(true);
      fixture.detectChanges();

      expect(byTestId('dossier-quote')?.querySelector('cmn-skeleton')).not.toBeNull();
    });

    it('shows the price alone when the quote has no day change', () => {
      mockStore.quoteHeader.set({priceText: '$189.30', changeText: null, direction: 'flat'});
      fixture.detectChanges();

      expect(byTestId('dossier-price')).not.toBeNull();
      expect(byTestId('dossier-day-change')).toBeNull();
    });
  });

  it('shows only the error alert when the dossier fails to load', () => {
    mockStore.dossierErrorMessage.set('Failed to load asset dossier.');
    fixture.detectChanges();

    expect(root().querySelector('cmn-alert')?.textContent).toContain(
      'Failed to load asset dossier.'
    );
    expect(byTestId('dossier-skeleton')).toBeNull();
    expect(root().textContent).not.toContain('Updated');
  });

  it('renders the dossier, not the skeleton, once loaded', () => {
    fixture.detectChanges();

    expect(byTestId('dossier-skeleton')).toBeNull();
    expect(root().textContent).toContain('Updated');
  });

  it('leaves the symbol and the way back to the top bar', () => {
    fixture.detectChanges();

    expect(byTestId('dossier-back')).toBeNull();
    expect(root().querySelector('h1')).toBeNull();
    expect(root().textContent).toContain('Updated');
  });

  it('formats money through the money pipe and names the provider', () => {
    fixture.detectChanges();

    const text = root().textContent ?? '';
    expect(text).toContain('$431.94');
    expect(text).not.toContain('431.94 USD');
    expect(text).toContain('Interactive Brokers');
  });

  it('links the position provider to the investment accounts list', () => {
    fixture.detectChanges();

    expect(byTestId('position-provider-link')?.getAttribute('href')).toBe('/accounts/investments');
  });

  it('links an analyst action to its source only when one exists', () => {
    fixture.detectChanges();

    const links = root().querySelectorAll<HTMLAnchorElement>('[data-testid="analyst-action-link"]');
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('https://example.com/needham');
    expect(root().textContent).toContain('Barclays');
  });

  it('keeps tax lots collapsed behind a counted summary', () => {
    fixture.detectChanges();

    const lots = byTestId('tax-lots') as HTMLDetailsElement;
    expect(lots.open).toBe(false);
    expect(lots.querySelector('summary')?.textContent).toContain('Tax lots (1)');
  });

  it('renders invalidation triggers as sentences', () => {
    fixture.detectChanges();

    const text = root().textContent ?? '';
    expect(text).toContain('Revenue growth (YoY) falls below 30.0%');
    expect(text).not.toContain('revenue_yoy');
  });

  it('shows "Read more" only when the thesis has further paragraphs', () => {
    fixture.detectChanges();
    expect(byTestId('thesis-toggle')).toBeNull();

    mockStore.thesisHasMore.set(true);
    fixture.detectChanges();
    expect(byTestId('thesis-toggle')?.textContent).toContain('Read more');

    byTestId('thesis-toggle')?.querySelector('button')?.click();
    expect(mockStore.toggleThesisExpanded).toHaveBeenCalled();
  });

  it('renders news as title, source and relative time without the summary', () => {
    fixture.detectChanges();

    const text = root().textContent ?? '';
    expect(text).toContain('Memory prices climb');
    expect(text).toContain('Wire · 3d ago');
    expect(text).not.toContain('should not render');
  });

  it('renders the recommendation trend as one verdict over a stacked bar', () => {
    fixture.detectChanges();

    expect(byTestId('recommendation-verdict')?.textContent).toContain('17 of 26 analysts');
    expect(byTestId('recommendation-verdict')?.textContent).toContain('rate it Buy');
    expect(byTestId('recommendation-bar')?.children).toHaveLength(4);
    const list = byTestId('trend-list');
    expect(list?.textContent).toContain('Strong buy');
    expect(list?.textContent).toContain('12');
  });

  describe('hero', () => {
    it('opens the page with the position strip, not a position card', () => {
      fixture.detectChanges();

      const hero = byTestId('dossier-hero');
      expect(hero).not.toBeNull();
      expect(hero?.querySelector('[data-testid="position-strip"]')).not.toBeNull();
      expect(byTestId('position-value')?.textContent).toContain('$431.94');
      expect(byTestId('position-pnl')?.textContent).toContain('$31.94');
      expect(byTestId('position-pnl')?.textContent).toContain('7.99%');
      expect(byTestId('position-pnl')?.className).toContain('text-gain');
      expect(root().textContent).not.toContain('Current Value');
    });

    it('shows the share of book only when the API reports it', () => {
      fixture.detectChanges();
      expect(byTestId('position-share')).toBeNull();

      mockStore.dossier.set({
        ...DOSSIER,
        position: {...POSITION, shareOfBookPercent: 12.34},
      });
      fixture.detectChanges();
      expect(byTestId('position-share')?.textContent).toContain('12.3%');
    });

    it('colours a loss with the loss token', () => {
      mockStore.dossier.set({
        ...DOSSIER,
        position: {...POSITION, unrealizedPnlUsd: -20, unrealizedPnlPercent: -4.5},
      });
      fixture.detectChanges();

      expect(byTestId('position-pnl')?.className).toContain('text-loss');
    });

    it('names the company with its sector and industry', () => {
      mockStore.dossier.set({
        ...DOSSIER,
        valuation: {
          ticker: 'DRAM',
          notApplicable: false,
          price: 43,
          isStale: false,
          metrics: {
            trailingPe: {
              value: null,
              fiveYearAvg: null,
              historyWindowYears: null,
              historyUnavailable: true,
            },
            forwardPe: {
              value: null,
              fiveYearAvg: null,
              historyWindowYears: null,
              historyUnavailable: true,
            },
            evToEbitda: {
              value: null,
              fiveYearAvg: null,
              historyWindowYears: null,
              historyUnavailable: true,
            },
            dividendYield: {
              value: 0.0295,
              fiveYearAvg: 0.03,
              historyWindowYears: 5,
              historyUnavailable: false,
            },
          },
          consensusTarget: null,
          impliedUpsidePct: null,
          peerSet: {
            name: 'sector:Technology (default)',
            peers: [{ticker: 'MU', forwardPe: 9.1, evToEbitda: null}],
          },
          sources: [],
          retrievedAt: '2026-09-01T08:00:00Z',
          name: 'Dram Memory Corp',
          sector: 'Technology',
          industry: 'Semiconductors',
          priceAsOf: null,
        },
      });
      fixture.detectChanges();

      expect(byTestId('dossier-name')?.textContent).toContain('Dram Memory Corp');
      expect(byTestId('dossier-profile')?.textContent).toContain(
        'DRAM · Technology · Semiconductors'
      );
      const text = root().textContent ?? '';
      expect(text).toContain('2.95%');
      expect(text).toContain('Versus Technology peers');
      expect(byTestId('valuation-peer')?.textContent).toContain('MU');
      expect(byTestId('valuation-peer-subject')?.textContent).toContain('DRAM');
    });

    it('dates the stale valuation tag by the price it was quoted at', () => {
      mockStore.dossier.set({
        ...DOSSIER,
        valuation: {...STALE_VALUATION, priceAsOf: '2026-08-28T20:00:00Z'},
      });
      fixture.detectChanges();

      expect(byTestId('valuation-stale')?.textContent).toMatch(/Stale\s*·\s*as of\s+Aug 28, 2026/);
    });

    it('leaves the stale valuation tag undated when the price date is unknown', () => {
      mockStore.dossier.set({...DOSSIER, valuation: STALE_VALUATION});
      fixture.detectChanges();

      expect(byTestId('valuation-stale')?.textContent?.trim()).toBe('Stale');
    });

    it('falls back to the symbol when the source gave no company name', () => {
      fixture.detectChanges();

      expect(byTestId('dossier-name')?.textContent).toContain('DRAM');
      expect(byTestId('dossier-profile')?.textContent).toContain('Equities');
    });
  });

  describe('section chips', () => {
    it('lists only the sections that exist and scrolls to one on click', () => {
      fixture.detectChanges();

      const chips = Array.from(
        byTestId('dossier-chips')?.querySelectorAll<HTMLElement>('cmn-chip') ?? []
      );
      expect(chips.map(c => c.textContent?.trim())).toEqual(['Analysts', 'Plan', 'News', 'Data']);

      const target = root().ownerDocument.getElementById('analyst-coverage');
      const scrollSpy = vi.fn();
      if (target) {
        target.scrollIntoView = scrollSpy;
      }
      chips[0].querySelector('button')?.click();
      expect(scrollSpy).toHaveBeenCalled();
    });
  });

  it('writes analyst actions and the news provider in words', () => {
    mockStore.dossier.set({
      ...DOSSIER,
      analysts: {
        ...DOSSIER_ANALYSTS,
        recentActions: [{...DOSSIER_ANALYSTS.recentActions[0], actionType: 'TargetChange'}],
      },
      recentNews: [{...DOSSIER.recentNews[0], source: 'yahoo:DRAM'}],
    });
    fixture.detectChanges();

    const text = root().textContent ?? '';
    expect(text).toContain('Target changed');
    expect(text).not.toContain('TargetChange');
    expect(text).toContain('Yahoo Finance');
    expect(text).not.toContain('yahoo:DRAM');
  });

  describe('Ledger read (Owner: ai.use)', () => {
    it('shows a single line with the action when nothing is generated', () => {
      fixture.detectChanges();

      expect(byTestId('ledger-read-empty')).not.toBeNull();
      expect(byTestId('ledger-read-card')).toBeNull();
      expect(byTestId('ledger-read-generate')).not.toBeNull();
    });

    it('shows the card with the narrative once generated', () => {
      mockStore.ledgerReadNarrative.set('A read.');
      fixture.detectChanges();

      expect(byTestId('ledger-read-card')).not.toBeNull();
      expect(byTestId('ledger-read-narrative')?.textContent).toContain('A read.');
      expect(byTestId('ledger-read-regenerate')).not.toBeNull();
      expect(byTestId('ledger-read-empty')).toBeNull();
    });

    it('renders **bold** as emphasis with no literal asterisks', () => {
      mockStore.ledgerReadNarrative.set('Up **31%** this year.');
      fixture.detectChanges();

      const narrative = byTestId('ledger-read-narrative');
      expect(narrative?.querySelector('strong')?.textContent).toBe('31%');
      expect(narrative?.textContent).not.toContain('*');
    });

    it('sanitises unsafe HTML in the narrative', () => {
      mockStore.ledgerReadNarrative.set('<img src=x onerror="alert(1)"> hi');
      fixture.detectChanges();

      const narrative = byTestId('ledger-read-narrative');
      expect(narrative?.querySelector('img')).toBeNull();
      expect(narrative?.textContent).toContain('<img');
    });

    it('shows coverage and trend period as plain language', () => {
      fixture.detectChanges();

      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('In your research universe');
      expect(text).not.toContain('inUniverse');
      expect(text).toContain('Aug 2026');
      expect(text).not.toContain('2026-08-01');
    });

    it('generates on click', () => {
      fixture.detectChanges();

      byTestId('ledger-read-generate')?.querySelector('button')?.click();
      expect(mockStore.generateLedgerRead).toHaveBeenCalledWith({symbol: 'DRAM', force: false});
    });
  });

  describe('Ledger read (Member: no ai.use)', () => {
    beforeEach(() => canUseAi.set(false));

    it('hides the block entirely', () => {
      mockStore.ledgerReadNarrative.set('A read.');
      fixture.detectChanges();

      expect(byTestId('ledger-read-card')).toBeNull();
      expect(byTestId('ledger-read-empty')).toBeNull();
      expect(byTestId('ledger-read-generate')).toBeNull();
      expect(byTestId('ledger-read-unavailable')).toBeNull();
      expect(root().textContent).not.toContain('not available for your account');
    });

    it('still renders the rest of the dossier', () => {
      fixture.detectChanges();

      expect(root().textContent).toContain('Tax lots (1)');
    });
  });

  describe('radar signals', () => {
    const signalAt = (day: number, severity: string) => ({
      timestamp: `2026-09-0${day}T00:00:00Z`,
      scanner: 'volume',
      signalType: 'volume_spike',
      severity,
      payload: {},
    });

    beforeEach(() => {
      mockStore.dossier.set({
        ...DOSSIER,
        radarSignals: [signalAt(1, 'info'), signalAt(2, 'notable'), signalAt(3, 'alerted')],
      });
      fixture.detectChanges();
    });

    it('tones each tag by the severity the API emits', () => {
      const tags = fixture.debugElement
        .queryAll(By.directive(TagComponent))
        .filter(t => ['info', 'notable', 'alerted'].includes(t.nativeElement.textContent.trim()));
      const toneOf = (label: string): string | undefined =>
        tags.find(t => t.nativeElement.textContent.trim() === label)?.componentInstance.variant();

      expect(tags).toHaveLength(3);
      expect(toneOf('alerted')).toBe('error');
      expect(toneOf('notable')).toBe('warning');
      expect(toneOf('info')).toBe('info');
    });

    it('draws a trend that rises with severity instead of a flat line', () => {
      const values = fixture.componentInstance.radarTrendPoints()?.map(p => p.value);

      expect(values).toEqual([5, 15, 25]);
    });
  });

  describe('stage 2 sections', () => {
    const rule = (key: string, passed: boolean | null) => ({
      key,
      label: `Rule ${key}`,
      passed,
      value: passed === null ? null : 12.2,
      threshold: 10,
      unit: 'percent' as const,
      note: key === 'fit_b' ? 'IPS cap' : null,
    });
    const axis = (key: DossierCheckAxisKey, a: boolean | null, b: boolean | null) => ({
      key,
      label: key,
      passed: [a, b].filter(x => x === true).length,
      evaluated: [a, b].filter(x => x !== null).length,
      rules: [rule(`${key}_a`, a), rule(`${key}_b`, b)],
    });
    const STOCK: AssetDossierDto = {
      ...DOSSIER,
      position: {
        ...POSITION,
        shareOfBookPercent: 12.2,
        positionCapPercent: 10,
        returns: {
          unrealizedUsd: -3690,
          realizedUsd: 519,
          dividendsUsd: 0,
          currencyUsd: null,
          totalUsd: -3171,
          coverage: 'complete',
          averageCostNative: 40,
          costCurrency: 'USD',
          fillCount: 4,
        },
      },
      valuation: {
        ...STALE_VALUATION,
        isStale: false,
        sources: ['yahoo-quote', 'sec-edgar'],
        consensusTarget: 50,
        impliedUpsidePct: 16.3,
        targetLow: 35,
        targetHigh: 60,
        analystCount: 12,
        currency: 'USD',
        marketCap: 4.2e9,
      },
      performance: {
        lastClose: 43,
        yearChangePct: 22.2,
        high52Week: 44,
        low52Week: 30,
        fromHighPct: -2.3,
        benchmarkYearChangePct: 14,
        currency: 'USD',
        asOf: '2026-10-13',
      },
      checks: {
        axes: [
          axis('analysts', true, true),
          axis('growth', true, false),
          axis('value', false, null),
          axis('momentum', true, true),
          axis('fit', false, true),
        ],
      },
      userTarget: {targetPrice: 55, currency: 'USD', updatedAt: '2026-10-13T00:00:00Z'},
    };
    const CHART: AssetChartDto = {
      symbol: 'DRAM',
      range: '1Y',
      currency: 'USD',
      points: [
        {date: '2025-10-13', close: 35, benchmark: 100},
        {date: '2026-04-13', close: 40, benchmark: 107},
        {date: '2026-10-13', close: 43, benchmark: 114},
      ],
      benchmarkLabel: 'S&P 500',
      trades: [{date: '2026-04-13', side: 'buy', quantity: 10, price: 40, currency: 'USD'}],
      averageCost: 40,
      changePct: 22.9,
      benchmarkChangePct: 14,
      source: 'yahoo-chart',
      retrievedAt: '2026-10-13T00:00:00Z',
    };

    beforeEach(() => {
      mockStore.dossier.set(STOCK);
      mockStore.chart.set(CHART);
    });

    it('puts the thesis hook, the performance strip and the size-vs-cap bar in the hero', () => {
      fixture.detectChanges();

      expect(byTestId('dossier-hook')?.textContent).toContain('12.2% of book');
      expect(byTestId('performance-strip')?.textContent).toContain('+22.2%');
      expect(byTestId('size-cap-text')?.textContent).toContain('10');
      expect(byTestId('size-cap-mark')).not.toBeNull();
    });

    it('shows the wheel next to the checks and opens an axis to its rules and values', () => {
      fixture.detectChanges();

      expect(byTestId('checks-wheel')).not.toBeNull();
      expect(byTestId('dossier-checks')?.textContent).toContain('6');
      const fit = byTestId('check-axis-fit');
      expect(fit?.textContent).toContain('Rule fit_b');
      expect(fit?.textContent).toContain('IPS cap');
      expect(root().querySelectorAll('[data-testid="tick-strip"]').length).toBeGreaterThan(0);
    });

    it('labels the tick strip by its count', () => {
      fixture.detectChanges();

      const labels = Array.from(root().querySelectorAll('[data-testid="tick-strip"]')).map(el =>
        el.getAttribute('aria-label')
      );
      expect(labels).toContain('value: 0 of 2 checks pass');
    });

    it('draws the chart with its change, the trade markers and the key', () => {
      fixture.detectChanges();

      expect(byTestId('chart-change')?.textContent).toContain('+22.9%');
      expect(root().querySelectorAll('[data-testid="chart-trade"]')).toHaveLength(1);
      expect(byTestId('chart-key')?.textContent).toContain('average cost');
      expect(byTestId('chart-key')?.textContent).toContain('Buy');
    });

    it('asks the store for a new range when a tab is picked', () => {
      fixture.detectChanges();

      byTestId('chart-ranges')?.dispatchEvent(
        new CustomEvent('lk-segmented-change', {detail: {value: '5Y'}})
      );

      expect(mockStore.selectChartRange).toHaveBeenCalledWith('5Y');
    });

    it('breaks the return down into its legs', () => {
      fixture.detectChanges();

      expect(byTestId('returns-total')?.textContent).toContain('-$3,171');
      expect(byTestId('returns-realized')?.textContent).toContain('$519');
      expect(byTestId('returns-currency')?.textContent).toContain('Not available');
    });

    it('places the price, the analyst target and the user target on the band with the source', () => {
      fixture.detectChanges();

      expect(byTestId('target-range')).not.toBeNull();
      expect(byTestId('target-marker-price')).not.toBeNull();
      expect(byTestId('target-marker-analyst')).not.toBeNull();
      expect(byTestId('target-marker-mine')).not.toBeNull();
      expect(byTestId('target-source')?.textContent).toContain('12 analysts');
      expect(byTestId('my-target-value')?.textContent).toContain('$55.00');
    });

    it('shows each fundamental or the reason it is missing', () => {
      fixture.detectChanges();

      expect(byTestId('fundamental-market-cap')?.textContent).toContain('$4.20B');
      expect(byTestId('fundamental-ev-ebitda')?.textContent).toContain('not reported');
    });

    it('opens, clears and edits the user target through the store', () => {
      fixture.detectChanges();

      byTestId('my-target-clear')?.querySelector('button')?.click();
      byTestId('my-target-edit')?.querySelector('button')?.click();

      expect(mockStore.clearTarget).toHaveBeenCalled();
      expect(mockStore.openTargetEditor).toHaveBeenCalled();
    });

    it('saves a valid draft and flags an invalid one', () => {
      mockStore.isEditingTarget.set(true);
      mockStore.targetDraft.set('abc');
      fixture.detectChanges();

      expect(byTestId('my-target')?.textContent).toContain('Enter a price above zero.');

      mockStore.targetDraft.set('60');
      fixture.detectChanges();
      byTestId('my-target-save')?.querySelector('button')?.click();
      byTestId('my-target-cancel')?.querySelector('button')?.click();

      expect(mockStore.saveTarget).toHaveBeenCalled();
      expect(mockStore.closeTargetEditor).toHaveBeenCalled();
    });

    it('shows a failed save under the editor', () => {
      mockStore.targetErrorMessage.set('Failed to save your target.');
      fixture.detectChanges();

      expect(byTestId('my-target-error')?.textContent).toContain('Failed to save your target.');
    });

    it('marks ownership as not available and lists the data status', () => {
      fixture.detectChanges();

      expect(byTestId('ownership')?.textContent).toContain('Not available');
      expect(byTestId('data-status-valuation')?.textContent).toContain('Yahoo');
      expect(byTestId('data-status-chart')).not.toBeNull();
      expect(byTestId('data-status-ownership')?.textContent?.trim()).toBe(
        'Ownership Not available - no free source for holder data'
      );
    });

    it('hides valuation, analysts and ownership for crypto', () => {
      mockStore.dossier.set({
        ...STOCK,
        valuation: {...(STOCK.valuation as ValuationSnapshotDto), notApplicable: true},
        analysts: null,
        checks: {axes: [axis('momentum', true, false), axis('fit', true, true)]},
      });
      fixture.detectChanges();

      expect(byTestId('fundamentals')).toBeNull();
      expect(byTestId('ownership')).toBeNull();
      expect(byTestId('checks-wheel')).toBeNull();
      expect(byTestId('check-axis-momentum')).not.toBeNull();
      expect(byTestId('target-source')).toBeNull();
      expect(byTestId('valuation')).not.toBeNull();
    });
  });
});
