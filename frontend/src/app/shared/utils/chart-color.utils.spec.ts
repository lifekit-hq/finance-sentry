import {describe, expect, it} from 'vitest';

import {CATEGORICAL_SERIES_STEPS} from '../constants/chart/chart.constants';
import {ChartColorUtils} from './chart-color.utils';

describe('ChartColorUtils.series', () => {
  it('returns the chart-series token of the step', () => {
    expect(ChartColorUtils.series(9)).toBe('var(--color-chart-series-9)');
  });
});

describe('ChartColorUtils.categorical', () => {
  it('starts at the accent series step', () => {
    expect(ChartColorUtils.categorical(0)).toBe('var(--color-chart-series-1)');
  });

  it('gives every mark in one cycle a distinct colour', () => {
    const colors = CATEGORICAL_SERIES_STEPS.map((_, i) => ChartColorUtils.categorical(i));
    expect(new Set(colors).size).toBe(CATEGORICAL_SERIES_STEPS.length);
  });

  it('wraps after the last categorical step', () => {
    expect(ChartColorUtils.categorical(CATEGORICAL_SERIES_STEPS.length)).toBe(
      ChartColorUtils.categorical(0)
    );
  });

  it('never hands out the slate tail colour', () => {
    expect(CATEGORICAL_SERIES_STEPS.map((_, i) => ChartColorUtils.categorical(i))).not.toContain(
      'var(--color-chart-series-9)'
    );
  });

  it('maps a negative index into the cycle', () => {
    expect(ChartColorUtils.categorical(-1)).toBe(
      ChartColorUtils.categorical(CATEGORICAL_SERIES_STEPS.length - 1)
    );
  });
});
