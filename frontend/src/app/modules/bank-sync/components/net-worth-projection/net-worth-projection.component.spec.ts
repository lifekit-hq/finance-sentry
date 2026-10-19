import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it} from 'vitest';

import {type NetWorthProjection} from '../../models/projection/projection.model';
import {netWorthProjectionComputed} from '../../store/net-worth-projection/net-worth-projection.computed';
import {netWorthProjection} from '../../store/net-worth-projection/net-worth-projection.fixtures';
import {NetWorthProjectionStore} from '../../store/net-worth-projection/net-worth-projection.store';
import {NetWorthProjectionComponent} from './net-worth-projection.component';

function render(projection: Nullable<NetWorthProjection>, isLoading = false) {
  const computeds = netWorthProjectionComputed({projection: signal(projection)});
  TestBed.overrideComponent(NetWorthProjectionComponent, {
    set: {
      providers: [
        {
          provide: NetWorthProjectionStore,
          useValue: {...computeds, isLoading: signal(isLoading)},
        },
      ],
    },
  });
  const fixture = TestBed.createComponent(NetWorthProjectionComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('NetWorthProjectionComponent', () => {
  it('shows the central figure with its range and the assumptions in words', () => {
    const el = render(netWorthProjection());
    const line = el.querySelector('[data-testid="net-worth-projection"]')?.textContent ?? '';
    expect(line).toContain('In 12 months: about');
    expect(line).toContain('$22,789');
    expect(line).toContain('$22,312 to $23,270');
    const assumptions =
      el.querySelector('[data-testid="net-worth-projection-assumptions"]')?.textContent ?? '';
    expect(assumptions).toContain('5% real (after-inflation) annual return');
    expect(assumptions).toContain('Hypothetical, not a guarantee.');
  });

  it("holds the figure's line with a skeleton while loading", () => {
    const el = render(null, true);
    expect(el.querySelectorAll('cmn-skeleton')).toHaveLength(1);
    expect(el.querySelector('[data-testid="net-worth-projection"]')).toBeNull();
  });

  it('shows the stale-data notice when a source is stale', () => {
    const el = render(netWorthProjection({hasStaleSleeves: true}));
    expect(el.querySelector('[data-testid="net-worth-projection-stale"]')?.textContent).toContain(
      'projection is an estimate'
    );
  });

  it('omits the stale-data notice when no source is stale', () => {
    const el = render(netWorthProjection());
    expect(el.querySelector('[data-testid="net-worth-projection-stale"]')).toBeNull();
  });

  it('renders nothing below the complete-month minimum', () => {
    const el = render(netWorthProjection({status: 'InsufficientHistory', projectedNetWorth: null}));
    expect(el.querySelector('[data-testid="net-worth-projection"]')).toBeNull();
    expect(el.querySelector('cmn-skeleton')).toBeNull();
  });
});
