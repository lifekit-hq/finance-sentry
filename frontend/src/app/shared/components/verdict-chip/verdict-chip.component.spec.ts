import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it} from 'vitest';

import {type VerdictWord} from '../../models/scorecard/scorecard.model';
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
});
