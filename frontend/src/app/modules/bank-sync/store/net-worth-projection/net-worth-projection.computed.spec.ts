import {signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {type NetWorthProjection} from '../../models/projection/projection.model';
import {netWorthProjectionComputed} from './net-worth-projection.computed';
import {netWorthProjection} from './net-worth-projection.fixtures';

function build(projection: Nullable<NetWorthProjection>) {
  return netWorthProjectionComputed({projection: signal(projection)});
}

describe('netWorthProjectionComputed', () => {
  it('is hidden and empty before the projection loads', () => {
    const computeds = build(null);
    expect(computeds.visible()).toBe(false);
    expect(computeds.central()).toBe('');
    expect(computeds.range()).toBe('');
    expect(computeds.assumptions()).toBe('');
    expect(computeds.horizonMonths()).toBe(0);
    expect(computeds.hasStaleSleeves()).toBe(false);
  });

  it('is hidden below the complete-month minimum', () => {
    expect(
      build(netWorthProjection({status: 'InsufficientHistory', projectedNetWorth: null})).visible()
    ).toBe(false);
  });

  it('flags stale sleeves only while the projection is visible', () => {
    expect(build(netWorthProjection({hasStaleSleeves: true})).hasStaleSleeves()).toBe(true);
    expect(build(netWorthProjection()).hasStaleSleeves()).toBe(false);
    expect(
      build(
        netWorthProjection({
          status: 'InsufficientHistory',
          projectedNetWorth: null,
          hasStaleSleeves: true,
        })
      ).hasStaleSleeves()
    ).toBe(false);
  });

  it('shows the central figure, its range and its assumptions in words', () => {
    const computeds = build(netWorthProjection());
    expect(computeds.visible()).toBe(true);
    expect(computeds.central()).toBe('$22,789');
    expect(computeds.range()).toBe('$22,312 to $23,270');
    expect(computeds.assumptions()).toContain('the range uses 2% to 8%');
    expect(computeds.horizonMonths()).toBe(12);
  });
});
