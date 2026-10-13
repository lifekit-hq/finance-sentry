import {computed, signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {provideRouter} from '@angular/router';
import {TagComponent} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {
  type AssetDossierDto,
  type DossierAnalystsSection,
  type DossierHeroProfile,
  type DossierPositionSection,
  type DossierQuoteHeader,
  type DossierSectionChip,
  type MetricValue,
  type RecommendationSummary,
  type ThesisDto,
  type ValuationSnapshotDto,
} from '../../models/dossier/dossier.model';
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

describe('AssetDossierComponent', () => {
  let fixture: ComponentFixture<AssetDossierComponent>;
  let canUseAi: ReturnType<typeof signal<boolean>>;
  let mockStore: {
    isDossierLoading: ReturnType<typeof signal<boolean>>;
    dossierErrorMessage: ReturnType<typeof signal<string>>;
    dossier: ReturnType<typeof signal<AssetDossierDto | null>>;
    hasDossierSections: ReturnType<typeof signal<boolean>>;
    heroProfile: ReturnType<typeof computed<DossierHeroProfile | null>>;
    sectionChips: ReturnType<typeof computed<DossierSectionChip[]>>;
    recommendationSummary: ReturnType<typeof computed<RecommendationSummary | null>>;
    ledgerRead: ReturnType<typeof signal<{generatedAt: string | null} | null>>;
    isLedgerReadLoading: ReturnType<typeof signal<boolean>>;
    isLedgerReadStale: ReturnType<typeof signal<boolean>>;
    ledgerReadNarrative: ReturnType<typeof signal<string>>;
    ledgerReadErrorMessage: ReturnType<typeof signal<string>>;
    visibleThesisParagraphs: ReturnType<typeof signal<string[]>>;
    thesisHasMore: ReturnType<typeof signal<boolean>>;
    isThesisExpanded: ReturnType<typeof signal<boolean>>;
    quoteHeader: ReturnType<typeof signal<DossierQuoteHeader | null>>;
    isQuoteLoading: ReturnType<typeof signal<boolean>>;
    toggleThesisExpanded: ReturnType<typeof vi.fn>;
    generateLedgerRead: ReturnType<typeof vi.fn>;
  };

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const byTestId = (id: string): HTMLElement | null =>
    root().querySelector<HTMLElement>(`[data-testid="${id}"]`);

  beforeEach(async () => {
    canUseAi = signal(true);
    const dossier = signal<AssetDossierDto | null>(DOSSIER);
    mockStore = {
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
      ledgerRead: signal(null),
      isLedgerReadLoading: signal(false),
      isLedgerReadStale: signal(false),
      ledgerReadNarrative: signal(''),
      ledgerReadErrorMessage: signal(''),
      visibleThesisParagraphs: signal(['Memory cycle is turning.']),
      thesisHasMore: signal(false),
      isThesisExpanded: signal(false),
      quoteHeader: signal<DossierQuoteHeader | null>(null),
      isQuoteLoading: signal(false),
      toggleThesisExpanded: vi.fn(),
      generateLedgerRead: vi.fn(),
    };

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
      expect(chips.map(c => c.textContent?.trim())).toEqual(['Thesis', 'Analysts', 'News']);

      const target = root().ownerDocument.getElementById('analyst-coverage');
      const scrollSpy = vi.fn();
      if (target) {
        target.scrollIntoView = scrollSpy;
      }
      chips[1].querySelector('button')?.click();
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
});
