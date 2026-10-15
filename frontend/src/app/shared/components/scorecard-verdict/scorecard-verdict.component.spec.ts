import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it} from 'vitest';

import {VERDICT_STRUCTURE_BASIS_NOTE} from '../../constants/scorecard/scorecard.constants';
import {ScorecardFixtures} from '../../models/scorecard/scorecard.fixtures';
import {type ScorecardDto} from '../../models/scorecard/scorecard.model';
import {ScorecardVerdictComponent} from './scorecard-verdict.component';

describe('ScorecardVerdictComponent', () => {
  const render = (scorecard: ScorecardDto): HTMLElement => {
    const fixture = TestBed.createComponent(ScorecardVerdictComponent);
    fixture.componentRef.setInput('scorecard', scorecard);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (host: HTMLElement, testId: string): string =>
    host.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(() => TestBed.resetTestingModule());

  it('reads the verdict word, the rules version and the rule that fired', () => {
    const host = render(ScorecardFixtures.scorecard());

    expect(text(host, 'verdict-word')).toBe('AVOID');
    expect(host.textContent).toContain('Rules v1');
    expect(text(host, 'verdict-fired-rule')).toBe('Rule extended_and_rich.');
  });

  it('keeps the drill-down closed until the owner opens it', () => {
    const host = render(ScorecardFixtures.scorecard());
    const why = host.querySelector<HTMLDetailsElement>('[data-testid="verdict-why"]');

    expect(why?.open).toBe(false);
    expect(why?.querySelector('summary')?.textContent).toContain('Why AVOID');
  });

  it('opens into the four axes, each with its headline and the facts behind it', () => {
    const host = render(ScorecardFixtures.scorecard());

    for (const axis of ['Structure', 'Fundamentals', 'Crowding', 'Valuation']) {
      expect(host.querySelector(`[data-testid="verdict-axis-${axis}"]`)).not.toBeNull();
    }
    expect(text(host, 'verdict-axis-Structure')).toContain('88 / 100 (raw 92)');
    const facts = Array.from(
      host.querySelectorAll('[data-testid="verdict-axis-Structure"] dt'),
      dt => `${dt.textContent?.trim()}: ${dt.nextElementSibling?.textContent?.trim()}`
    );
    expect(facts).toContain('Extension from 50-day MA: +24.0%');
    expect(text(host, 'verdict-axis-Valuation')).toContain('P/E +29.2% vs its own average');
    expect(text(host, 'verdict-axis-Crowding')).toContain('Extended at or above');
  });

  it('says so when an axis has no facts at all', () => {
    const card = ScorecardFixtures.scorecard();
    const host = render({...card, valuation: {...card.valuation, notApplicable: true}});

    expect(text(host, 'verdict-axis-Valuation')).toContain('No facts on this axis.');
  });

  it('lists the whole rule table and marks only the first match as fired', () => {
    const host = render(ScorecardFixtures.scorecard());
    const rules = Array.from(host.querySelectorAll('[data-testid^="verdict-rule-"]'));

    expect(rules.map(r => r.getAttribute('data-testid'))).toEqual([
      'verdict-rule-outside_universe',
      'verdict-rule-weak_structure',
      'verdict-rule-extended_and_rich',
      'verdict-rule-buy',
      'verdict-rule-buy_fundamentals_missing',
      'verdict-rule-otherwise',
    ]);
    expect(
      rules
        .filter(r => r.getAttribute('data-fired') === 'true')
        .map(r => r.getAttribute('data-testid'))
    ).toEqual(['verdict-rule-extended_and_rich']);
    expect(text(host, 'verdict-rule-extended_and_rich')).toContain('Fired');
    expect(text(host, 'verdict-rule-buy')).toContain('Also matched');
    expect(text(host, 'verdict-rule-weak_structure')).not.toContain('matched');
  });

  it('shows each condition with its threshold, the value it read and whether it was met', () => {
    const host = render(ScorecardFixtures.scorecard());

    expect(text(host, 'verdict-rule-extended_and_rich')).toContain(
      'P/E premium to its 5-year average > 0% · read 29.2%: met'
    );
    expect(text(host, 'verdict-rule-weak_structure')).toContain(
      'Adjusted structure < 30 · read 88: not met'
    );
  });

  it('names which structure value the thresholds read', () => {
    const card = ScorecardFixtures.scorecard();

    expect(text(render(card), 'verdict-structure-basis')).toBe(
      VERDICT_STRUCTURE_BASIS_NOTE.adjusted
    );
    TestBed.resetTestingModule();
    expect(
      text(
        render({...card, verdict: {...card.verdict, structureBasis: 'raw'}}),
        'verdict-structure-basis'
      )
    ).toBe(VERDICT_STRUCTURE_BASIS_NOTE.raw);
  });

  it('shows no fired-rule line when the table names no such rule', () => {
    const card = ScorecardFixtures.scorecard();
    const host = render({...card, verdict: {...card.verdict, firedRuleId: 'gone'}});

    expect(host.querySelector('[data-testid="verdict-fired-rule"]')).toBeNull();
  });

  it('carries no composite number anywhere', () => {
    const host = render(ScorecardFixtures.scorecard());

    expect(host.textContent?.toLowerCase()).not.toMatch(/composite|overall score|total score/);
  });
});
