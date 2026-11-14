import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it} from 'vitest';

import {VERDICT_STRUCTURE_BASIS_NOTE} from '../../../../shared/constants/scorecard/scorecard.constants';
import {ScorecardFixtures} from '../../../../shared/models/scorecard/scorecard.fixtures';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';
import {VerdictTrailComponent} from './verdict-trail.component';

describe('VerdictTrailComponent', () => {
  const render = (scorecard: ScorecardDto): HTMLElement => {
    const fixture = TestBed.createComponent(VerdictTrailComponent);
    fixture.componentRef.setInput('scorecard', scorecard);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (host: HTMLElement, testId: string): string =>
    host.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(() => TestBed.resetTestingModule());

  it('numbers the rules in the order they are tried, down to the one that fired', () => {
    const host = render(ScorecardFixtures.scorecard());

    const steps = [...host.querySelectorAll('[data-testid^="verdict-trail-step-"]')];
    expect(steps.map(s => s.getAttribute('data-testid'))).toEqual([
      'verdict-trail-step-1',
      'verdict-trail-step-2',
      'verdict-trail-step-3',
    ]);
    expect(steps.map(s => s.getAttribute('data-fired'))).toEqual(['false', 'false', 'true']);
  });

  it('says of each step whether it matched, and which one decides the word', () => {
    const host = render(ScorecardFixtures.scorecard());

    expect(text(host, 'verdict-trail-step-2')).toContain('Does not match');
    expect(text(host, 'verdict-trail-step-3')).toContain('Matches: this decides the word');
  });

  it('shows each condition with the value it read and whether it was met', () => {
    const host = render(ScorecardFixtures.scorecard());

    const step = text(host, 'verdict-trail-step-2');
    expect(step).toContain('Adjusted structure < 30');
    expect(step).toContain('read 88');
    expect(step).toContain('not met');
    expect(text(host, 'verdict-trail-step-3')).toContain('Crowding is Extended');
  });

  it('never lists a rule after the one that fired', () => {
    const host = render(ScorecardFixtures.scorecard());

    expect(host.textContent).not.toContain('Adjusted structure ≥ 50');
  });

  it('names the structure basis the thresholds read, beside the order of the rules', () => {
    const host = render(ScorecardFixtures.scorecard());

    expect(text(host, 'verdict-trail-intro')).toContain('first one that matches decides');
    expect(text(host, 'verdict-trail-intro')).toContain(VERDICT_STRUCTURE_BASIS_NOTE.adjusted);
  });
});
