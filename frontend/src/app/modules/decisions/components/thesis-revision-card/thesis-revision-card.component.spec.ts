import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {beforeEach, describe, expect, it} from 'vitest';

import {type ThesisDto} from '../../../../shared/models/thesis/thesis.model';
import {ThesisRevisionFixtures} from '../../../../shared/models/thesis-revision/thesis-revision.fixtures';
import {
  type ThesisRevisionDto,
  type ThesisRevisionVerdict,
} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {
  THESIS_REVISION_CLOSE_TEXT,
  THESIS_REVISION_NO_BEFORE_TEXT,
  THESIS_REVISION_PROMOTE_DEFAULTS_TEXT,
  THESIS_REVISION_WALL_TEXT,
} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisRevisionCardComponent} from './thesis-revision-card.component';

const {revision, content, thesis, trigger} = ThesisRevisionFixtures;

describe('ThesisRevisionCardComponent', () => {
  const render = (
    rev: ThesisRevisionDto,
    before: ThesisDto | null = thesis(),
    linkTicker = false
  ) => {
    TestBed.configureTestingModule({providers: [provideRouter([])]});
    const fixture = TestBed.createComponent(ThesisRevisionCardComponent);
    fixture.componentRef.setInput('revision', rev);
    fixture.componentRef.setInput('thesis', before);
    fixture.componentRef.setInput('linkTicker', linkTicker);
    const verdicts: ThesisRevisionVerdict[] = [];
    fixture.componentInstance.decide.subscribe(v => verdicts.push(v));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const text = (testId: string): string =>
      host.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? '';
    return {host, verdicts, text};
  };

  beforeEach(() => TestBed.resetTestingModule());

  it('names the change, the proposer, the rationale and the expiry', () => {
    const {host, text} = render(revision());

    expect(host.textContent).toContain('Update thesis');
    expect(host.textContent).toContain('CBRS');
    expect(host.textContent).toContain('Ledger');
    expect(text('thesis-revision-why')).toContain('names two anchor customers');
    expect(text('thesis-revision-expires')).toBe('Expires Nov 24, 2026');
  });

  it('puts the thesis text before and after side by side', () => {
    const {text} = render(revision());

    expect(text('thesis-revision-before')).toBe('Wafer-scale inference wins on cost per token.');
    expect(text('thesis-revision-after')).toContain('two hyperscaler anchors');
  });

  it('says there is no thesis yet on a promotion', () => {
    const {text} = render(
      revision({kind: 'Promote', proposed: content({thesisText: 'A new case.'})}),
      null
    );

    expect(text('thesis-revision-before')).toBe(THESIS_REVISION_NO_BEFORE_TEXT);
    expect(text('thesis-revision-after')).toBe('A new case.');
  });

  it('shows the trigger diff: added in, removed struck out, kept plain', () => {
    const kept = trigger();
    const dropped = trigger({metric: 'net_margin', threshold: 0.1});
    const added = trigger({metric: 'price_drawdown', direction: 'greaterThan', threshold: 0.4});
    const {host} = render(
      revision({kind: 'TriggerChange', proposed: content({invalidationTriggers: [kept, added]})}),
      thesis({invalidationTriggers: [kept, dropped]})
    );

    const items = Array.from(
      host.querySelectorAll<HTMLElement>('[data-testid="thesis-revision-diff"] li')
    );
    expect(items.map(i => i.textContent?.trim())).toEqual([
      '+ Price drawdown rises above 40.0%',
      '− Net margin falls below 10.0%',
      'Gross margin falls below 35.0%',
    ]);
    expect(items[0].className).toContain('text-gain');
    expect(items[1].className).toContain('line-through');
    expect(host.querySelector('[data-testid="thesis-revision-change-text"]')).toBeNull();
  });

  it('says a promotion without text or triggers takes the scorecard defaults', () => {
    const {text, host} = render(revision({kind: 'Promote', proposed: content()}), null);

    expect(text('thesis-revision-defaults')).toBe(THESIS_REVISION_PROMOTE_DEFAULTS_TEXT);
    expect(host.querySelector('[data-testid^="thesis-revision-change-"]')).toBeNull();
  });

  it('says a close retires the thesis, with nothing to diff', () => {
    const {text, host} = render(revision({kind: 'Close', proposed: content()}));

    expect(text('thesis-revision-close')).toBe(THESIS_REVISION_CLOSE_TEXT);
    expect(host.querySelector('[data-testid^="thesis-revision-change-"]')).toBeNull();
  });

  it('links a stored article, and lists the other evidence as plain labels', () => {
    const {host} = render(revision());

    const items = Array.from(host.querySelectorAll('[data-testid="thesis-revision-evidence"] li'));
    expect(items).toHaveLength(2);
    const link = items[0].querySelector('a');
    expect(link?.getAttribute('href')).toBe('https://example.com/cbrs');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(items[0].textContent).toContain('Cerebras files for IPO');
    expect(items[1].querySelector('a')).toBeNull();
    expect(items[1].textContent).toContain('Filing 0001-26-1');
  });

  it('shows no evidence section when the proposal cites none', () => {
    const {host} = render(revision({evidenceLinks: null}));

    expect(host.querySelector('[data-testid="thesis-revision-evidence"]')).toBeNull();
  });

  it('states that confirming writes the thesis and never a position', () => {
    const {text} = render(revision());

    expect(text('thesis-revision-wall')).toContain(THESIS_REVISION_WALL_TEXT);
  });

  it('hands the verdict to the host', () => {
    const {host, verdicts} = render(revision());

    host.querySelector<HTMLElement>('[data-testid="thesis-revision-reject"] button')?.click();
    host.querySelector<HTMLElement>('[data-testid="thesis-revision-confirm"] button')?.click();

    expect(verdicts).toEqual(['reject', 'confirm']);
  });

  it('links the ticker to its dossier only when asked', () => {
    expect(
      render(revision(), thesis(), true).host.querySelector('header a')?.getAttribute('href')
    ).toBe('/assets/CBRS');

    TestBed.resetTestingModule();
    expect(render(revision()).host.querySelector('header a')).toBeNull();
  });
});
