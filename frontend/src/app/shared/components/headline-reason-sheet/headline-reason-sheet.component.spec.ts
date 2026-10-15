import {TestBed} from '@angular/core/testing';
import {ErrorMessageService} from '@lifekit-hq/core';
import {CMN_DRAWER_DATA} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it} from 'vitest';

import {HEADLINE_REASON_MESSAGES} from '../../../core/errors/headline-reasons.registry';
import {HEADLINE_REASON_FALLBACK} from '../../constants/headline/headline.constants';
import {
  type Headline,
  type HeadlineBadge,
  type HeadlineReasonSheetData,
} from '../../models/headline/headline.model';
import {HeadlineReasonSheetComponent} from './headline-reason-sheet.component';

function render(headline: Partial<Headline>, badge: HeadlineBadge): HTMLElement {
  const data: HeadlineReasonSheetData = {
    headline: {
      amount: 100,
      percent: null,
      method: 'VALUE',
      scope: 'umbrella',
      window: {to: '2026-10-15'},
      quality: 'DEGRADED',
      reasons: [],
      currency: 'USD',
      ...headline,
    },
    badge,
  };
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      {provide: CMN_DRAWER_DATA, useValue: data},
      {
        provide: ErrorMessageService,
        useValue: {resolve: (code: string) => HEADLINE_REASON_MESSAGES[code] ?? null},
      },
    ],
  });
  const fixture = TestBed.createComponent(HeadlineReasonSheetComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('HeadlineReasonSheetComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('lists each reason in words from the registry, with its source', () => {
    const el = render(
      {
        reasons: [{code: 'STALE_SOURCE', source: 'crypto'}, {code: 'FX_UNKNOWN_1TO1'}],
      },
      {kind: 'stale', label: 'Stale'}
    );
    const items = Array.from(el.querySelectorAll('li'));
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain('Crypto');
    expect(items[0].textContent).toContain(HEADLINE_REASON_MESSAGES['STALE_SOURCE']);
    expect(items[1].textContent).toContain(HEADLINE_REASON_MESSAGES['FX_UNKNOWN_1TO1']);
  });

  it('falls back to a generic line for a code the registry does not know, without breaking', () => {
    const el = render(
      {reasons: [{code: 'SOMETHING_NEW'}, {code: 'STALE_SOURCE', source: 'banking'}]},
      {kind: 'stale', label: 'Stale'}
    );
    const items = Array.from(el.querySelectorAll('li'));
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain(HEADLINE_REASON_FALLBACK);
    expect(items[0].textContent).not.toContain('SOMETHING_NEW');
  });

  it('shows asOf and coverage when the figure is not complete', () => {
    const el = render(
      {asOf: '2026-10-14T08:00:00Z', coverage: 0.8, reasons: [{code: 'NO_COST_BASIS'}]},
      {kind: 'partial', label: 'Partial'}
    );
    expect(el.querySelector('[data-testid="headline-as-of"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="headline-coverage"]')?.textContent).toContain('80%');
  });

  it('leaves asOf and coverage out when the headline has none', () => {
    const el = render({reasons: [{code: 'NO_SPLIT'}]}, {kind: 'unavailable', label: 'Unavailable'});
    expect(el.querySelector('[data-testid="headline-as-of"]')).toBeNull();
    expect(el.querySelector('[data-testid="headline-coverage"]')).toBeNull();
  });

  it('opens with a sentence that matches the badge', () => {
    const el = render({reasons: []}, {kind: 'estimated', label: 'Estimated'});
    expect(el.querySelector('[data-testid="headline-reason-summary"]')?.textContent).toContain(
      'estimate'
    );
    expect(el.querySelector('[data-testid="headline-reasons"]')).toBeNull();
  });
});
