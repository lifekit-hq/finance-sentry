import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type Headline} from '../../models/headline/headline.model';
import {HeadlineBadgeComponent} from './headline-badge.component';
import {HeadlineReasonSheetService} from './headline-reason-sheet.service';

function headline(overrides: Partial<Headline> = {}): Headline {
  return {
    amount: 100,
    percent: null,
    method: 'VALUE',
    scope: 'umbrella',
    window: {to: '2026-10-15'},
    quality: 'COMPLETE',
    reasons: [],
    currency: 'USD',
    ...overrides,
  };
}

describe('HeadlineBadgeComponent', () => {
  const open = vi.fn();
  let fixture: ComponentFixture<HeadlineBadgeComponent>;

  const render = (value: Nullable<Headline>, label = 'Net worth'): HTMLElement => {
    fixture = TestBed.createComponent(HeadlineBadgeComponent);
    fixture.componentRef.setInput('headline', value);
    fixture.componentRef.setInput('label', label);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };
  const button = (el: HTMLElement): HTMLButtonElement | null =>
    el.querySelector<HTMLButtonElement>('[data-testid="headline-badge"]');

  beforeEach(() => {
    open.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{provide: HeadlineReasonSheetService, useValue: {open}}],
    });
  });

  it('renders nothing for a complete figure', () => {
    expect(button(render(headline()))).toBeNull();
  });

  it('renders nothing when there is no headline', () => {
    expect(button(render(null))).toBeNull();
  });

  it('reads Estimated for an estimated figure', () => {
    const el = button(render(headline({quality: 'ESTIMATED', reasons: [{code: 'FX_TODAY_RATE'}]})));
    expect(el?.textContent?.trim()).toBe('Estimated');
    expect(el?.dataset['badge']).toBe('estimated');
  });

  it('reads Stale for a degraded figure with a stale source', () => {
    const el = button(
      render(headline({quality: 'DEGRADED', reasons: [{code: 'STALE_SOURCE', source: 'crypto'}]}))
    );
    expect(el?.textContent?.trim()).toBe('Stale');
  });

  it('reads Partial for a degraded figure without one', () => {
    const el = button(
      render(headline({quality: 'DEGRADED', reasons: [{code: 'FX_UNKNOWN_1TO1'}]}))
    );
    expect(el?.textContent?.trim()).toBe('Partial');
  });

  it('reads Unavailable for a figure with no amount', () => {
    const el = button(
      render(headline({amount: null, quality: 'UNAVAILABLE', reasons: [{code: 'NO_SPLIT'}]}))
    );
    expect(el?.textContent?.trim()).toBe('Unavailable');
  });

  it('is a labelled button with a 44px hit area', () => {
    const el = button(render(headline({quality: 'ESTIMATED'})));
    expect(el?.tagName).toBe('BUTTON');
    expect(el?.getAttribute('type')).toBe('button');
    expect(el?.getAttribute('aria-label')).toBe('Net worth: estimated. Show why');
    expect(el?.className).toContain('min-h-11');
    expect(el?.className).toContain('min-w-11');
  });

  it('opens the reason sheet with the headline, its badge and the label', () => {
    const value = headline({
      quality: 'DEGRADED',
      reasons: [{code: 'STALE_SOURCE', source: 'crypto'}],
    });
    button(render(value))?.click();
    expect(open).toHaveBeenCalledWith(value, {kind: 'stale', label: 'Stale'}, 'Net worth');
  });
});
