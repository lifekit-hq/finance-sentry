import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {beforeEach, describe, expect, it} from 'vitest';

import {
  type RecommendationDto,
  type RecommendationVerdict,
} from '../../../../shared/models/recommendation/recommendation.model';
import {ScorecardFixtures} from '../../../../shared/models/scorecard/scorecard.fixtures';
import {RecommendationCardComponent} from './recommendation-card.component';

function mkRecommendation(overrides: Partial<RecommendationDto> = {}): RecommendationDto {
  return {
    id: 'rec-1',
    ticker: 'GRAB',
    action: 'Trim',
    status: 'Pending',
    createdBy: 'Ledger',
    origin: 'Ledger',
    triggerKind: 'RiskRule',
    triggerRef: 'concentration',
    rationale: 'Above the single-name cap.',
    confidence: 'Medium',
    invalidationTrigger: 'Back under the cap.',
    evidence: [],
    sizeUsd: 2080,
    sizeShares: 660,
    shareOfBookBeforePct: 12.2,
    shareOfBookAfterPct: 9.5,
    limitPrice: 3.13,
    limitCurrency: 'USD',
    venue: 'IBKR',
    orderText: 'SELL 660 GRAB / LIMIT 3.13 USD DAY / IBKR',
    createdAt: '2026-10-12T09:00:00Z',
    expiresAt: '2026-10-19T09:00:00Z',
    decidedAt: null,
    decisionNote: null,
    execution: 'Pending',
    executedAt: null,
    subjectReturnPct: null,
    benchmarkReturnPct: null,
    excessReturnPct: null,
    outcomeScoredAt: null,
    ...overrides,
  };
}
describe('RecommendationCardComponent', () => {
  const render = (recommendation: RecommendationDto, linkTicker = false) => {
    TestBed.configureTestingModule({providers: [provideRouter([])]});
    const fixture = TestBed.createComponent(RecommendationCardComponent);
    fixture.componentRef.setInput('recommendation', recommendation);
    fixture.componentRef.setInput('linkTicker', linkTicker);
    const verdicts: RecommendationVerdict[] = [];
    fixture.componentInstance.decide.subscribe(v => verdicts.push(v));
    fixture.detectChanges();
    return {host: fixture.nativeElement as HTMLElement, verdicts};
  };

  beforeEach(() => TestBed.resetTestingModule());

  it('reads the move, its size, why, the invalidation trigger and the expiry', () => {
    const {host} = render(mkRecommendation());

    expect(host.querySelector('[data-testid="recommendation-card-GRAB"]')).not.toBeNull();
    expect(host.querySelector('article')?.getAttribute('aria-label')).toBe('Trim GRAB');
    expect(host.querySelector('[data-testid="recommendation-size"]')?.textContent?.trim()).toBe(
      'Sell 660 shares (about $2,080), taking it from 12.2% to 9.5% of book'
    );
    expect(host.textContent).toContain('Above the single-name cap.');
    expect(host.textContent).toContain('Back under the cap.');
    expect(host.textContent).toContain('Oct 19, 2026');
    expect(host.querySelector('[data-testid="recommendation-confidence"]')?.textContent).toContain(
      'Medium confidence'
    );
    expect(host.textContent).toContain('Ledger');
  });

  it('names a rule check as its origin and lists the cited evidence', () => {
    const {host} = render(
      mkRecommendation({
        origin: 'Rules',
        evidence: [
          {kind: 'RiskRule', label: 'Single name above 10% of book', refId: 'concentration'},
          {kind: 'Thesis', label: 'No thesis on file', refId: null},
        ],
      })
    );

    expect(host.textContent).toContain('Rule check');
    const items = Array.from(
      host.querySelectorAll('[data-testid="recommendation-evidence"] li')
    ).map(li => [
      li.querySelector('cmn-tag')?.textContent?.trim(),
      li.querySelector(':scope > span')?.textContent,
    ]);
    expect(items).toEqual([
      ['Risk rule', 'Single name above 10% of book'],
      ['Thesis', 'No thesis on file'],
    ]);
  });

  it('shows no evidence section when none is cited', () => {
    const {host} = render(mkRecommendation());

    expect(host.querySelector('[data-testid="recommendation-evidence"]')).toBeNull();
  });

  it('links the ticker to its dossier only when asked', () => {
    expect(render(mkRecommendation()).host.querySelector('a')).toBeNull();

    TestBed.resetTestingModule();
    const linked = render(mkRecommendation(), true).host.querySelector('a');
    expect(linked?.getAttribute('href')).toBe('/assets/GRAB');
  });

  it('emits the verdict the owner picks', () => {
    const {host, verdicts} = render(mkRecommendation());

    host.querySelector<HTMLElement>('[data-testid="recommendation-reject"] button')?.click();
    host.querySelector<HTMLElement>('[data-testid="recommendation-confirm"] button')?.click();

    expect(verdicts).toEqual(['reject', 'confirm']);
  });

  it('shows no research verdict without a scorecard', () => {
    const {host} = render(mkRecommendation());

    expect(host.querySelector('[data-testid="recommendation-verdict"]')).toBeNull();
  });

  it('opens a research verdict with its drill-down when a scorecard is given', () => {
    TestBed.configureTestingModule({providers: [provideRouter([])]});
    const fixture = TestBed.createComponent(RecommendationCardComponent);
    fixture.componentRef.setInput('recommendation', mkRecommendation());
    fixture.componentRef.setInput('scorecard', ScorecardFixtures.scorecard({symbol: 'GRAB'}));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const verdict = host.querySelector('[data-testid="recommendation-verdict"]');

    expect(verdict?.textContent).toContain('Research verdict');
    expect(verdict?.querySelector('[data-testid="verdict-word"]')?.textContent?.trim()).toBe(
      'AVOID'
    );
    expect(verdict?.querySelector('[data-testid="verdict-why"]')).not.toBeNull();
  });
});
