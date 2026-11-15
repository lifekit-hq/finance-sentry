import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it} from 'vitest';

import {ScorecardFixtures} from '../../models/scorecard/scorecard.fixtures';
import {type ScorecardDto} from '../../models/scorecard/scorecard.model';
import {LookThroughComponent} from './look-through.component';

describe('LookThroughComponent', () => {
  const render = (scorecard: ScorecardDto): HTMLElement => {
    const fixture = TestBed.createComponent(LookThroughComponent);
    fixture.componentRef.setInput('scorecard', scorecard);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (host: HTMLElement, testId: string): string =>
    host.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(() => TestBed.resetTestingModule());

  it('shows the basis of an ETF verdict: the share, the source and each holding with its word', () => {
    const host = render(ScorecardFixtures.etf());

    expect(text(host, 'look-through-headline')).toBe(
      '60% of the top 10 weight reads BUY or better · BUY needs more than 50%'
    );
    expect(text(host, 'look-through-source')).toContain('Yahoo Finance');
    expect(text(host, 'look-through-source')).toContain('read Nov 14, 2026');
    const holdings = host.querySelectorAll('[data-testid="look-through-holding"]');
    expect(holdings).toHaveLength(10);
    expect(holdings[0].textContent).toContain('H1');
    expect(holdings[0].textContent).toContain('10%');
    expect(holdings[0].textContent).toContain('BUY');
    expect(holdings[9].textContent).toContain('WAIT');
    expect(host.querySelector('[data-testid="look-through-degraded"]')).toBeNull();
  });

  it('warns visibly when only part of the top 10 could be scored', () => {
    const base = ScorecardFixtures.lookThrough();
    const host = render(
      ScorecardFixtures.etf({
        lookThrough: ScorecardFixtures.lookThrough({
          status: 'partial',
          note: '1 of 10 listed holdings could not be scored and count against the fund.',
          holdings: [
            {...base.holdings[0], word: null, firedRuleId: null},
            ...base.holdings.slice(1),
          ],
        }),
      })
    );

    expect(text(host, 'look-through-degraded')).toBe(
      '1 of 10 listed holdings could not be scored and count against the fund.'
    );
    expect(host.querySelectorAll('[data-testid="look-through-holding"]')[0].textContent).toContain(
      'NOT SCORED'
    );
  });

  it('says the look-through was not applied, with no holdings listed, when none were found', () => {
    const host = render(
      ScorecardFixtures.etf({
        lookThrough: ScorecardFixtures.lookThrough({
          status: 'unavailable',
          source: null,
          asOf: null,
          coveredWeightPct: 0,
          buyWeightPct: 0,
          buyWeightSharePct: null,
          holdings: [],
          note: 'Holdings for this fund could not be read from Yahoo or an issuer file, so the look-through was not applied.',
        }),
      })
    );

    expect(text(host, 'look-through-headline')).toBe(
      'No holdings found: the look-through was not applied.'
    );
    expect(text(host, 'look-through-degraded')).toContain('could not be read from Yahoo');
    expect(host.querySelector('[data-testid="look-through-holdings"]')).toBeNull();
    expect(host.querySelector('[data-testid="look-through-source"]')).toBeNull();
  });

  it('renders nothing for a name that is not an ETF', () => {
    const host = render(ScorecardFixtures.scorecard());

    expect(host.querySelector('[data-testid="look-through"]')).toBeNull();
  });
});
