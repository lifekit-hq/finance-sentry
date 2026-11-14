import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it} from 'vitest';

import {NOT_ADVICE_NOTICE} from '../../../../shared/constants/legal/legal.constants';
import {type VerdictDisplay} from '../../../../shared/models/preferences/app-preferences.model';
import {ScorecardFixtures} from '../../../../shared/models/scorecard/scorecard.fixtures';
import {
  type ScorecardDto,
  type VerdictReasonDto,
  type VerdictWord,
} from '../../../../shared/models/scorecard/scorecard.model';
import {type ThesisDto} from '../../models/dossier/dossier.model';
import {VerdictBlockComponent} from './verdict-block.component';

const THESIS: ThesisDto = {
  id: 't1',
  ticker: 'ABCD',
  thesisText: 'A plan.',
  keyDataPoints: [],
  catalysts: [],
  invalidationTriggers: [
    {
      metric: 'gross_margin',
      direction: 'lessThan',
      threshold: 0.35,
      proxyTicker: null,
      consecutivePeriods: 2,
      periodType: 'quarter',
    },
  ],
  createdAt: '2026-10-01T00:00:00Z',
  updatedAt: '2026-10-01T00:00:00Z',
  brokenAt: null,
  brokenReason: null,
  entryPrice: null,
};

const REASON: VerdictReasonDto = {
  axis: 'Structure',
  status: 'Pass',
  text: 'Structure 78 clears the BUY bar of 50.',
  decisive: false,
};

/** A scorecard whose verdict is the given word, with generic reasons. */
function card(word: VerdictWord, turnsBuyWhen: string | null = null): ScorecardDto {
  const base = ScorecardFixtures.scorecard();
  return {
    ...base,
    verdict: {
      ...base.verdict,
      word,
      reasons: [
        REASON,
        {...REASON, axis: 'Fundamentals', text: 'Fundamentals 72 clear the BUY bar of 50.'},
        {...REASON, axis: 'Crowding', status: 'Fail', text: 'Crowding is Extended.'},
        {...REASON, axis: 'Valuation', status: 'Missing', text: 'Valuation: nothing to read.'},
      ],
      turnsBuyWhen,
    },
  };
}

describe('VerdictBlockComponent', () => {
  const render = (
    scorecard: ScorecardDto,
    inputs: {held?: boolean; thesis?: ThesisDto | null; display?: VerdictDisplay} = {}
  ): HTMLElement => {
    const fixture = TestBed.createComponent(VerdictBlockComponent);
    fixture.componentRef.setInput('scorecard', scorecard);
    fixture.componentRef.setInput('held', inputs.held ?? false);
    fixture.componentRef.setInput('thesis', inputs.thesis ?? null);
    fixture.componentRef.setInput('display', inputs.display ?? 'simple');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (host: HTMLElement, testId: string): string =>
    host.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const has = (host: HTMLElement, testId: string): boolean =>
    host.querySelector(`[data-testid="${testId}"]`) !== null;

  beforeEach(() => TestBed.resetTestingModule());

  describe.each<[VerdictWord, string]>([
    ['StrongBuy', 'STRONG BUY'],
    ['Buy', 'BUY'],
    ['Wait', 'WAIT'],
    ['Avoid', 'AVOID'],
    ['NotScored', 'NOT SCORED'],
  ])('the word %s', (word, label) => {
    it('is shown once, labelled for new money', () => {
      const host = render(card(word));

      expect(host.querySelectorAll('[data-testid="verdict-word"]').length).toBe(1);
      expect(text(host, 'verdict-word')).toBe(label);
      expect(text(host, 'verdict-for-new-money')).toBe('for new money');
    });

    it('carries the not-advice notice', () => {
      expect(text(render(card(word)), 'verdict-not-advice')).toBe(NOT_ADVICE_NOTICE);
    });
  });

  it('lists each reason with its sentence and status', () => {
    const host = render(card('Wait'));

    const reasons = [...host.querySelectorAll('[data-testid="verdict-reason"]')];
    expect(reasons.map(r => r.getAttribute('data-status'))).toEqual([
      'Pass',
      'Pass',
      'Fail',
      'Missing',
    ]);
    expect(reasons[0].textContent).toContain('Structure 78 clears the BUY bar of 50.');
    expect(reasons[2].textContent).toContain('Falls short');
  });

  it('shows the turns-BUY line only when the backend sent one', () => {
    const waiting = render(card('Wait', 'structure reaches 50 (reads 39)'));
    const buying = render(card('Buy'));

    expect(text(waiting, 'verdict-turns-buy')).toBe(
      'Turns BUY when structure reaches 50 (reads 39).'
    );
    expect(has(buying, 'verdict-turns-buy')).toBe(false);
  });

  it('shows no reasons list for a verdict that carries none', () => {
    const empty: ScorecardDto = {...card('Wait'), verdict: {...card('Wait').verdict, reasons: []}};

    expect(has(render(empty), 'verdict-reasons')).toBe(false);
  });

  describe('the detailed display', () => {
    it('swaps the reasons for the numbered trail of rules that produced the word', () => {
      const host = render(card('Wait'), {display: 'detailed'});

      expect(has(host, 'verdict-reasons')).toBe(false);
      expect(has(host, 'verdict-trail')).toBe(true);
      expect(host.querySelectorAll('[data-testid^="verdict-trail-step-"]').length).toBeGreaterThan(
        0
      );
    });

    it('keeps the word, the turns-BUY line and the thesis, and shows the rule table only once', () => {
      const host = render(card('Wait', 'structure reaches 50 (reads 39)'), {
        display: 'detailed',
        thesis: THESIS,
      });

      expect(text(host, 'verdict-word')).toBe('WAIT');
      expect(text(host, 'verdict-turns-buy')).toContain('structure reaches 50');
      expect(has(host, 'verdict-thesis-state')).toBe(true);
      expect(has(host, 'verdict-rule-weak_structure')).toBe(false);
      expect(has(host, 'verdict-axis-Valuation')).toBe(true);
    });

    it('is not the default: the simple display keeps the reasons and the rule table', () => {
      const host = render(card('Wait'));

      expect(has(host, 'verdict-reasons')).toBe(true);
      expect(has(host, 'verdict-trail')).toBe(false);
      expect(has(host, 'verdict-rule-weak_structure')).toBe(true);
    });
  });

  describe('a held position', () => {
    it('says holding follows the thesis and kill-switch, never the word', () => {
      const host = render(card('Avoid'), {held: true, thesis: THESIS});

      expect(text(host, 'verdict-held-line')).toBe(
        'You hold this. Holding follows your thesis and its kill-switch, never the word.'
      );
    });

    it('shows no such line when the name is not held', () => {
      expect(has(render(card('Avoid'), {held: false, thesis: THESIS}), 'verdict-held-line')).toBe(
        false
      );
    });
  });

  describe('the thesis and kill-switch beside the word', () => {
    it('shows an active thesis with its armed kill rules', () => {
      const host = render(card('Buy'), {thesis: THESIS});

      expect(text(host, 'verdict-thesis-state')).toBe('Thesis active');
      expect(text(host, 'verdict-kill-switch')).toBe('Kill-switch armed: 1 rule');
      expect(text(host, 'verdict-kill-rules')).toBe('Gross margin falls below 35.0%');
      expect(has(host, 'verdict-thesis-prompt')).toBe(false);
    });

    it('shows a tripped kill-switch with its reason', () => {
      const host = render(card('Buy'), {
        thesis: {...THESIS, brokenAt: '2026-10-20T00:00:00Z', brokenReason: 'Margin fell.'},
      });

      expect(text(host, 'verdict-thesis-state')).toBe('Thesis invalidated');
      expect(text(host, 'verdict-kill-switch')).toBe('Kill-switch tripped');
      expect(text(host, 'verdict-broken-reason')).toBe('Margin fell.');
    });

    it('prompts to write a thesis when there is none, with no kill-switch tag', () => {
      const host = render(card('Wait'), {thesis: null});

      expect(text(host, 'verdict-thesis-state')).toBe('No thesis on file');
      expect(text(host, 'verdict-thesis-prompt')).toContain('Write a thesis');
      expect(has(host, 'verdict-kill-switch')).toBe(false);
    });
  });

  it('keeps the drill-down behind a closed disclosure, without repeating the word', () => {
    const host = render(card('Wait'));
    const why = host.querySelector<HTMLDetailsElement>('[data-testid="verdict-why"]');

    expect(why?.open).toBe(false);
    expect(host.querySelectorAll('[data-testid="verdict-word"]').length).toBe(1);
    expect(has(host, 'verdict-axis-Valuation')).toBe(true);
    expect(has(host, 'verdict-fired-rule')).toBe(false);
  });
});
