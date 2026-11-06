import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {NOT_ADVICE_NOTICE} from '../../../../shared/constants/legal/legal.constants';
import {UniverseScreenFixtures} from '../../models/universe-screen/universe-screen.fixtures';
import {PortfolioConstructionService} from '../../services/portfolio-construction.service';
import {UniverseScreenComponent} from './universe-screen.component';

describe('UniverseScreenComponent', () => {
  const getUniverse = vi.fn();

  let fixture: ComponentFixture<UniverseScreenComponent>;

  const render = async (screen = UniverseScreenFixtures.screen()): Promise<HTMLElement> => {
    getUniverse.mockReturnValue(of(screen));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {provide: PortfolioConstructionService, useValue: {getUniverse}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    fixture = TestBed.createComponent(UniverseScreenComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (host: HTMLElement, testId: string, index = 0): string =>
    host
      .querySelectorAll(`[data-testid="${testId}"]`)
      [index]?.textContent?.replace(/\s+/g, ' ')
      .trim() ?? '';
  const all = (host: HTMLElement, testId: string): Element[] => [
    ...host.querySelectorAll(`[data-testid="${testId}"]`),
  ];

  beforeEach(() => {
    TestBed.resetTestingModule();
    getUniverse.mockReset();
  });

  it('says the verdicts are informational, not advice', async () => {
    const host = await render();

    expect(text(host, 'construction-not-advice')).toBe(NOT_ADVICE_NOTICE);
  });

  it('lists every name with its verdict word, the four axes and the suggested weight', async () => {
    const host = await render();

    expect(all(host, 'universe-row')).toHaveLength(3);
    expect(all(host, 'universe-symbol').map(e => e.textContent?.trim())).toEqual([
      'AAA',
      'BBB',
      'CCC',
    ]);
    expect(text(host, 'universe-verdict', 0)).toBe('BUY');
    expect(text(host, 'universe-structure', 0)).toBe('Structure 68 (raw 64)');
    expect(text(host, 'universe-fundamentals', 0)).toBe('Fundamentals 72');
    expect(text(host, 'universe-crowding', 0)).toBe('Crowding Normal');
    expect(text(host, 'universe-valuation', 0)).toBe('P/E 28 vs 5y avg 24 (+16.7%)');
    expect(text(host, 'universe-weight', 0)).toBe('12%');
  });

  it('reads a missing axis as no data and gives the name no weight', async () => {
    const host = await render();

    expect(text(host, 'universe-fundamentals', 1)).toBe('Fundamentals no data');
    expect(all(host, 'universe-weight')).toHaveLength(1);
  });

  it('shows the sector of a name beside its symbol, and nothing for a name without one', async () => {
    const host = await render();

    expect(all(host, 'universe-sector').map(e => e.textContent?.trim())).toEqual([
      'Technology',
      'Technology',
    ]);
    expect(all(host, 'universe-sector')[0]?.getAttribute('title')).toBe(
      'Software - Infrastructure'
    );
  });

  it('shows a name that failed to score with its reason and no verdict', async () => {
    const host = await render();

    expect(text(host, 'universe-failed')).toBe('Not scored');
    expect(text(host, 'universe-failure')).toBe('the scorecard could not be read');
  });

  it('opens a row into its reason, the rule that fired and the rules version', async () => {
    const host = await render();

    expect(text(host, 'universe-reason', 0)).toContain('Passed the Growth gates with BUY');
    expect(text(host, 'universe-rule', 0)).toBe(
      'Verdict rule buy, rules v1: Adjusted structure at least 50 and fundamentals at least 50.'
    );
  });

  it('labels every applied limit with the source that set it', async () => {
    const host = await render();

    const constraints = text(host, 'universe-constraints');
    expect(constraints).toContain('Largest single position: 8% your risk rules');
    expect(constraints).toContain('Cash floor: 5% archetype default');
    expect(constraints).toContain('Most names held: 10 archetype default');
  });

  it('shows the sleeves of the suggested book', async () => {
    const host = await render();

    expect(text(host, 'universe-sleeve-Equities')).toBe('12%');
    expect(text(host, 'universe-sleeve-Cash')).toBe('88%');
  });

  it('explains a refusal instead of showing weights', async () => {
    const screen = UniverseScreenFixtures.screen();
    screen.construction.refusal =
      'Your IPS targets sum to 120%, over 100%: the policy cannot be built as written.';
    const host = await render(screen);

    expect(text(host, 'universe-refusal')).toContain('sum to 120%');
    expect(all(host, 'universe-sleeve-Equities')).toHaveLength(0);
  });

  it('narrows the list to one verdict word', async () => {
    const host = await render();

    (host.querySelector('[data-testid="universe-filter-Wait"] button') as HTMLElement).click();
    fixture.detectChanges();

    expect(all(host, 'universe-symbol').map(e => e.textContent?.trim())).toEqual(['BBB']);
  });

  it('says so when the filter matches nothing', async () => {
    const host = await render();

    (host.querySelector('[data-testid="universe-filter-StrongBuy"] button') as HTMLElement).click();
    fixture.detectChanges();

    expect(all(host, 'universe-row')).toHaveLength(0);
    expect(all(host, 'universe-no-match')).toHaveLength(1);
  });

  it('asks again for the archetype the viewer picks, and not for the one already shown', async () => {
    await render();

    fixture.componentInstance.chooseArchetype('growth');
    expect(getUniverse).toHaveBeenCalledTimes(1);

    fixture.componentInstance.chooseArchetype('balanced');
    expect(getUniverse).toHaveBeenLastCalledWith('balanced');
  });

  it('shows an explicit empty state when the universe has no names', async () => {
    const host = await render(UniverseScreenFixtures.screen({rows: []}));

    expect(host.textContent).toContain('No names to score yet');
    expect(all(host, 'universe-row')).toHaveLength(0);
  });

  it('notes when the universe was capped', async () => {
    const host = await render(UniverseScreenFixtures.screen({truncated: true}));

    expect(host.textContent).toContain('The universe is capped');
  });
});
