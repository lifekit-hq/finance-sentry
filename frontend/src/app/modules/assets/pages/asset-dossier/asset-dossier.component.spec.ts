import {signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {By} from '@angular/platform-browser';
import {provideRouter} from '@angular/router';
import {ASYNC_STATE_SHOW_DELAY_MS, TagComponent} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, onTestFinished, vi} from 'vitest';

import {ScorecardFixtures} from '../../../../shared/models/scorecard/scorecard.fixtures';
import {AuthStore} from '../../../auth/store/auth.store';
import {PriceChartComponent} from '../../components/price-chart/price-chart.component';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type DossierCheckAxisKey,
  type DossierPositionSection,
  type ValuationSnapshotDto,
} from '../../models/dossier/dossier.model';
import {DossierStore} from '../../store/dossier.store';
import {PriceChartStore} from '../../store/price-chart.store';
import {AssetDossierComponent} from './asset-dossier.component';
import {
  buildMockStore,
  DOSSIER,
  DOSSIER_ANALYSTS,
  mkRecommendation,
  POSITION,
  STALE_VALUATION,
} from './asset-dossier.fixtures';

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
      // The interactive chart has its own store, canvas and specs: this page only has to place it.
      .overrideComponent(PriceChartComponent, {
        set: {template: '', providers: [{provide: PriceChartStore, useValue: {}}], imports: []},
      })
      .compileComponents();

    fixture = TestBed.createComponent(AssetDossierComponent);
  });

  it('renders skeletons once a load outlasts the show delay', () => {
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    mockStore.isDossierLoading.set(true);
    fixture.detectChanges();
    vi.advanceTimersByTime(ASYNC_STATE_SHOW_DELAY_MS);
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

  describe('verdict header', () => {
    const SCORED = {...DOSSIER, scorecard: ScorecardFixtures.scorecard({symbol: DOSSIER.symbol})};
    const QUOTE = {
      ticker: 'DRAM',
      price: 189.3,
      previousClose: 187,
      changePct: 1.2299,
      currency: 'USD',
    };
    const header = (): HTMLElement | null => byTestId('dossier-verdict-header');

    it('has no header, and the hero keeps the price slot, without a scorecard', () => {
      fixture.detectChanges();

      expect(header()).toBeNull();
      expect(byTestId('dossier-quote')).not.toBeNull();
    });

    it('opens the page on the verdict word, ahead of the hero', () => {
      mockStore.dossier.set(SCORED);
      fixture.detectChanges();

      expect(header()?.textContent).toContain('Avoid');
      expect(header()?.nextElementSibling).toBe(byTestId('dossier-hero'));
    });

    it('keeps the word honest with "for new money" and the thesis state beside it', () => {
      mockStore.dossier.set(SCORED);
      fixture.detectChanges();

      const pills = Array.from(header()?.querySelectorAll('cmn-tag') ?? []).map(tag =>
        tag.textContent?.trim()
      );
      expect(pills).toEqual(['For new money', 'Thesis active']);
    });

    it('carries the fixed held line as the caveat on a held name', () => {
      mockStore.dossier.set(SCORED);
      fixture.detectChanges();

      expect(header()?.textContent).toContain('You hold this.');
    });

    it('carries what turns the word to BUY on a name that is not held', () => {
      mockStore.dossier.set({...SCORED, position: null});
      fixture.detectChanges();

      expect(header()?.textContent).toContain('Turns BUY when');
    });

    it('moves the price and the day change into the header, out of the hero', () => {
      mockStore.dossier.set(SCORED);
      mockStore.quote.set(QUOTE);
      mockStore.quoteHeader.set({priceText: '$189.30', changeText: '+1.23%', direction: 'up'});
      fixture.detectChanges();

      expect(header()?.textContent).toContain('$189.30');
      expect(header()?.textContent).toContain('+1.23%');
      expect(header()?.textContent).toContain('+$2.30');
      expect(byTestId('dossier-quote')).toBeNull();
      expect(byTestId('dossier-price')).toBeNull();
    });

    it('shows the verdict alone while the quote is missing', () => {
      mockStore.dossier.set(SCORED);
      fixture.detectChanges();

      expect(header()?.textContent).toContain('Avoid');
      expect(header()?.textContent).not.toContain('$');
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

  it("puts Ledger's pending proposal under the hero and hands the verdict to the store", () => {
    const recommendation = mkRecommendation();
    mockStore.dossier.set({...DOSSIER, pendingRecommendation: recommendation});
    fixture.detectChanges();

    const hero = byTestId('dossier-hero');
    const card = byTestId('recommendation-card-DRAM');
    expect(card).not.toBeNull();
    expect(hero?.nextElementSibling?.tagName.toLowerCase()).toBe('fns-recommendation-card');
    expect(card?.querySelector('a')).toBeNull();

    root().querySelector<HTMLElement>('[data-testid="recommendation-reject"] button')?.click();
    expect(mockStore.decideRecommendation).toHaveBeenCalledWith({
      recommendation,
      verdict: 'reject',
    });
    expect(byTestId('dossier-decision-recorded')).toBeNull();
  });

  it('shows no verdict card without a scorecard', () => {
    fixture.detectChanges();

    expect(byTestId('dossier-verdict')).toBeNull();
  });

  it('puts the research verdict first under the hero, above the proposal, opening into its axes and rules', () => {
    mockStore.dossier.set({
      ...DOSSIER,
      pendingRecommendation: mkRecommendation(),
      scorecard: ScorecardFixtures.scorecard({symbol: DOSSIER.symbol}),
    });
    fixture.detectChanges();

    const verdict = byTestId('dossier-verdict');
    expect(byTestId('dossier-hero')?.nextElementSibling).toBe(verdict);
    expect(verdict?.nextElementSibling?.tagName.toLowerCase()).toBe('fns-recommendation-card');
    expect(verdict?.querySelector('[data-testid="verdict-word"]')?.textContent?.trim()).toBe(
      'AVOID'
    );
    expect(verdict?.querySelector('[data-testid="verdict-for-new-money"]')).not.toBeNull();
    expect(verdict?.querySelectorAll('[data-testid="verdict-reason"]').length).toBe(4);
    expect(verdict?.querySelector('[data-testid="verdict-axis-Valuation"]')).not.toBeNull();
    expect(verdict?.querySelector('[data-testid="verdict-rule-extended_and_rich"]')).not.toBeNull();
    expect(verdict?.textContent).toContain('It places no trade.');
  });

  it('offers the simple and detailed displays and hands the choice to the verdict block', () => {
    mockStore.dossier.set({
      ...DOSSIER,
      scorecard: ScorecardFixtures.scorecard({symbol: DOSSIER.symbol}),
    });
    fixture.detectChanges();

    const options = [...root().querySelectorAll('[data-testid="verdict-display"] [role="radio"]')];
    expect(options.map(o => o.textContent?.trim())).toEqual(['Simple', 'Detailed']);
    expect(byTestId('verdict-trail')).toBeNull();

    mockStore.verdictDisplay.set('detailed');
    fixture.detectChanges();

    expect(byTestId('verdict-trail')).not.toBeNull();
    expect(byTestId('verdict-reasons')).toBeNull();
  });

  it('saves the display the owner picks through the store', () => {
    mockStore.dossier.set({
      ...DOSSIER,
      scorecard: ScorecardFixtures.scorecard({symbol: DOSSIER.symbol}),
    });
    fixture.detectChanges();

    const detailed = [
      ...root().querySelectorAll<HTMLElement>('[data-testid="verdict-display"] [role="radio"]'),
    ].find(o => o.textContent?.trim() === 'Detailed');
    detailed?.click();

    expect(mockStore.setVerdictDisplay).toHaveBeenCalledWith('detailed');
  });

  it('hands the held flag and the thesis to the verdict block', () => {
    const scorecard = ScorecardFixtures.scorecard({symbol: DOSSIER.symbol});
    mockStore.dossier.set({...DOSSIER, scorecard});
    fixture.detectChanges();

    expect(byTestId('verdict-held-line')).not.toBeNull();
    expect(byTestId('verdict-thesis-state')?.textContent?.trim()).toBe('Thesis active');

    mockStore.dossier.set({...DOSSIER, scorecard, position: null, thesis: null});
    fixture.detectChanges();

    expect(byTestId('verdict-held-line')).toBeNull();
    expect(byTestId('verdict-thesis-prompt')).not.toBeNull();
  });

  it('says what was recorded once the proposal is decided', () => {
    mockStore.decidedRecommendation.set(
      mkRecommendation({status: 'Rejected', decisionNote: 'Still a core holding.'})
    );
    fixture.detectChanges();

    const note = byTestId('dossier-decision-recorded');
    expect(note?.textContent).toContain('Decision recorded: rejected');
    expect(note?.textContent).toContain('Rejected: Still a core holding.');
    expect(note?.querySelector('a')?.getAttribute('href')).toBe('/ledger/decisions');
  });

  it('keeps the decisions link from someone without Ledger', () => {
    canUseAi.set(false);
    mockStore.decidedRecommendation.set(mkRecommendation({status: 'Confirmed'}));
    fixture.detectChanges();

    expect(byTestId('dossier-decision-recorded')?.querySelector('a')).toBeNull();
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

    expect(byTestId('position-provider-link')?.getAttribute('href')).toBe(
      '/accounts?provider=ibkr'
    );
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
      expect(byTestId('position-unit-price')?.textContent).toContain('$43.19');
      expect(byTestId('position-average-price')?.textContent).toContain('$40.00');
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

    it('asks the store for a new range when a chip is pressed', () => {
      fixture.detectChanges();

      byTestId('chart-ranges')?.dispatchEvent(
        new CustomEvent('lk-chip-row-change', {detail: {selected: ['5Y'], value: '5Y'}})
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

    it('keeps the price and your own target on the band for crypto and hides analysts, fundamentals, peers and ownership', () => {
      const stockValuation = STOCK.valuation as ValuationSnapshotDto;
      mockStore.dossier.set({
        ...STOCK,
        position: {...(STOCK.position as DossierPositionSection), assetClass: 'Crypto'},
        valuation: {
          ...stockValuation,
          peerSet: {
            name: 'sector:Technology (default)',
            peers: [{ticker: 'MU', forwardPe: 9.1, evToEbitda: null}],
          },
        },
        checks: {axes: [axis('momentum', true, false), axis('fit', true, true)]},
      });
      fixture.detectChanges();

      expect(byTestId('valuation')?.textContent).toContain('Price & your target');
      expect(byTestId('valuation')?.textContent).not.toContain('Is it cheap?');
      expect(byTestId('target-band')).not.toBeNull();
      expect(byTestId('target-marker-price')).not.toBeNull();
      expect(byTestId('target-marker-mine')).not.toBeNull();
      expect(byTestId('my-target')).not.toBeNull();
      expect(byTestId('target-marker-analyst')).toBeNull();
      expect(byTestId('target-range')).toBeNull();
      expect(byTestId('target-source')).toBeNull();
      expect(byTestId('fundamentals')).toBeNull();
      expect(byTestId('valuation-peers')).toBeNull();
      expect(byTestId('recommendation-verdict')).toBeNull();
      expect(byTestId('ownership')).toBeNull();
      expect(byTestId('checks-wheel')).toBeNull();
      expect(byTestId('check-axis-momentum')).not.toBeNull();
      const chips = Array.from(
        byTestId('dossier-chips')?.querySelectorAll<HTMLElement>('cmn-chip') ?? []
      ).map(c => c.textContent?.trim());
      expect(chips).toContain('Price & your target');
      expect(chips).not.toContain('Valuation');
      expect(chips).not.toContain('Analysts');
    });
  });
});
