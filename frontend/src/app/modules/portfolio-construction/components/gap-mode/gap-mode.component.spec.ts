import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {beforeEach, describe, expect, it} from 'vitest';

import {GapModeFixtures} from '../../models/gap-mode/gap-mode.fixtures';
import {type GapMode} from '../../models/gap-mode/gap-mode.model';
import {GapModeComponent} from './gap-mode.component';

describe('GapModeComponent', () => {
  let fixture: ComponentFixture<GapModeComponent>;

  const render = (gap: GapMode | null, inputs: {loading?: boolean; errorMessage?: string} = {}) => {
    TestBed.configureTestingModule({providers: [provideRouter([])]});
    fixture = TestBed.createComponent(GapModeComponent);
    fixture.componentRef.setInput('gap', gap);
    fixture.componentRef.setInput('loading', inputs.loading ?? false);
    fixture.componentRef.setInput('errorMessage', inputs.errorMessage ?? '');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const texts = (host: HTMLElement, testId: string): string[] =>
    [...host.querySelectorAll(`[data-testid="${testId}"]`)].map(
      e => e.textContent?.replace(/\s+/g, ' ').trim() ?? ''
    );

  beforeEach(() => TestBed.resetTestingModule());

  it('states each name as missing, short or over, with its status tag', () => {
    const host = render(GapModeFixtures.gap());

    expect(texts(host, 'gap-symbol')).toEqual(['CCC', 'BBB', 'AAA', 'ZZZ']);
    expect(texts(host, 'gap-status')).toEqual([
      'Overweight',
      'Missing',
      'Underweight',
      'Overweight',
    ]);
    expect(texts(host, 'gap-text')).toEqual([
      'over by 13 pts',
      'missing',
      'short by 3 pts',
      'over by 5 pts',
    ]);
  });

  it('shows the held and target amounts and the sector of each name', () => {
    const host = render(GapModeFixtures.gap());

    expect(texts(host, 'gap-figures')[1]).toBe('0% held, 12% target ($0.00 of $1,200.00)');
    expect(texts(host, 'gap-sector')).toEqual(['Technology', 'Energy', 'Technology', 'No sector']);
  });

  it('marks a held name outside the target and carries its verdict word', () => {
    const host = render(GapModeFixtures.gap());

    expect(texts(host, 'gap-outside')).toEqual(['Held, not in target']);
    expect(texts(host, 'gap-verdict')[3]).toBe('AVOID');
  });

  it('lists the sleeves and the sectors', () => {
    const host = render(GapModeFixtures.gap());

    expect(texts(host, 'gap-sleeve-Equities')).toEqual(['50% of 36%, over by 14 pts']);
    expect(texts(host, 'gap-sleeve-Cash')).toEqual(['50% of 64%, short by 14 pts']);
    expect(host.querySelector('[data-testid="gap-sectors"]')?.textContent).toContain('Technology');
  });

  it('says the figures are informational on every state it shows', () => {
    const host = render(GapModeFixtures.gap());

    expect(texts(host, 'gap-label')[0]).toContain('not a recommendation to buy or sell');
  });

  it('shows percentages only, with no amounts, when there is no book to size against', () => {
    const host = render(
      GapModeFixtures.gap({
        hasBook: false,
        bookValueUsd: 0,
        names: [
          GapModeFixtures.nameGap({
            status: 'Missing',
            currentPct: 0,
            gapPct: 12,
            currentUsd: 0,
            targetUsd: null,
            gapUsd: null,
          }),
        ],
      })
    );

    expect(texts(host, 'gap-no-book')).toHaveLength(1);
    expect(texts(host, 'gap-figures')[0]).toBe('0% held, 12% target');
  });

  it('shows the refusal and no gap when the policy cannot be built', () => {
    const host = render(
      GapModeFixtures.gap({refusal: 'Your IPS targets sum to 120%, over 100%.', names: []})
    );

    expect(texts(host, 'gap-refusal')[0]).toContain('over 100%');
    expect(texts(host, 'gap-name')).toEqual([]);
  });

  it('names a stale book', () => {
    const host = render(GapModeFixtures.gap({bookStale: true, staleSources: ['brokerage']}));

    expect(texts(host, 'gap-stale')[0]).toContain('brokerage');
  });

  it('shows a skeleton while loading and an error with nothing else when it failed', async () => {
    const loading = render(null, {loading: true});
    await fixture.whenStable();
    fixture.detectChanges();
    expect(texts(loading, 'gap-skeleton')).toHaveLength(1);
    TestBed.resetTestingModule();
    const failed = render(null, {errorMessage: 'Failed to load how your book compares.'});
    await fixture.whenStable();
    fixture.detectChanges();
    expect(failed.textContent).toContain('Failed to load how your book compares.');
    expect(texts(failed, 'gap-name')).toEqual([]);
  });
});
