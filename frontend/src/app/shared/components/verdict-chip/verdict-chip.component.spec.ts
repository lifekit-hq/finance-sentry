import {TestBed} from '@angular/core/testing';
import {ToastService} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {SCORECARD_GAP_COPY} from '../../constants/scorecard/scorecard.constants';
import {type ScorecardGapReason, type VerdictWord} from '../../models/scorecard/scorecard.model';
import {VerdictChipComponent} from './verdict-chip.component';

const WORDS: readonly [VerdictWord, string][] = [
  ['StrongBuy', 'STRONG BUY'],
  ['Buy', 'BUY'],
  ['Wait', 'WAIT'],
  ['Avoid', 'AVOID'],
  ['NotScored', 'NOT SCORED'],
];

describe('VerdictChipComponent', () => {
  const render = (word: VerdictWord): HTMLElement => {
    const fixture = TestBed.createComponent(VerdictChipComponent);
    fixture.componentRef.setInput('word', word);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => TestBed.resetTestingModule());

  it.each(WORDS)('reads %s as %s, labelled for new money for a screen reader', (word, label) => {
    const chip = render(word).querySelector('[data-testid="verdict-chip"]');

    expect(chip?.textContent?.trim()).toBe(label);
    expect(chip?.getAttribute('aria-label')).toBe(
      `Verdict for new money: ${label}. Informational only. Not financial advice.`
    );
    expect(chip?.getAttribute('title')).toBe('Informational only. Not financial advice.');
  });

  it('is a label, not a control: nothing inside it is focusable', () => {
    const host = render('Buy');

    expect(host.querySelector('a, button, [tabindex]')).toBeNull();
  });

  describe('with no verdict (issue #1414)', () => {
    const REASONS: readonly ScorecardGapReason[] = ['no_role', 'outside_universe', 'unreadable'];
    const info = vi.fn();

    beforeEach(() => {
      TestBed.configureTestingModule({providers: [{provide: ToastService, useValue: {info}}]});
    });

    const renderGap = (gap: ScorecardGapReason) => {
      const fixture = TestBed.createComponent(VerdictChipComponent);
      fixture.componentRef.setInput('gap', gap);
      fixture.detectChanges();
      return fixture.nativeElement as HTMLElement;
    };

    beforeEach(() => info.mockClear());

    it.each(REASONS)('shows a dash and says %s on hover and to a screen reader', gap => {
      const element = renderGap(gap);
      const chip = element.querySelector('[data-testid="verdict-chip-gap"]');
      const text = `${SCORECARD_GAP_COPY[gap].line} ${SCORECARD_GAP_COPY[gap].hint}`;

      expect(chip?.textContent?.trim()).toBe('—');
      expect(chip?.getAttribute('title')).toBe(text);
      expect(chip?.getAttribute('aria-label')).toBe(`No verdict. ${text}`);
      expect(element.querySelector('[data-testid="verdict-chip"]')).toBeNull();
    });

    it('says the reason in a toast on a tap, and does not follow the row link', () => {
      const chip = renderGap('no_role').querySelector<HTMLElement>(
        '[data-testid="verdict-chip-gap"]'
      );
      const click = new MouseEvent('click', {bubbles: true, cancelable: true});

      chip?.dispatchEvent(click);

      expect(info).toHaveBeenCalledWith(
        `${SCORECARD_GAP_COPY.no_role.line} ${SCORECARD_GAP_COPY.no_role.hint}`
      );
      expect(click.defaultPrevented).toBe(true);
    });

    it('shows nothing when there is neither a word nor a reason', () => {
      const fixture = TestBed.createComponent(VerdictChipComponent);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).children.length).toBe(0);
    });
  });
});
