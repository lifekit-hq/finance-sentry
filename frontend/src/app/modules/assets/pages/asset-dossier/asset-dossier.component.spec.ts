import {signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {AuthStore} from '../../../auth/store/auth.store';
import {
  type AssetDossierDto,
  type DossierQuoteHeader,
  type ThesisDto,
} from '../../models/dossier/dossier.model';
import {DossierStore} from '../../store/dossier.store';
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

const DOSSIER: AssetDossierDto = {
  symbol: 'DRAM',
  position: {
    provider: 'ibkr',
    quantity: 10,
    currentValueUsd: 431.94,
    costBasisUsd: 400,
    unrealizedPnlUsd: 31.94,
    unrealizedPnlPercent: 7.985,
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
  },
  thesis: THESIS,
  valuation: null,
  analysts: {
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
  },
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
    mockStore = {
      isDossierLoading: signal(false),
      dossierErrorMessage: signal(''),
      dossier: signal<AssetDossierDto | null>(DOSSIER),
      hasDossierSections: signal(true),
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
      expect(change?.className).toContain('text-status-success');
      expect(byTestId('dossier-price')?.className).not.toContain('text-status');
    });

    it('colours a loss as an error delta', () => {
      mockStore.quoteHeader.set({priceText: '$189.30', changeText: '-0.50%', direction: 'down'});
      fixture.detectChanges();

      expect(byTestId('dossier-day-change')?.className).toContain('text-status-error');
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

  it('renders the recommendation trend as a list', () => {
    fixture.detectChanges();

    const list = byTestId('trend-list');
    expect(list?.textContent).toContain('Strong buy');
    expect(list?.textContent).toContain('12');
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
});
