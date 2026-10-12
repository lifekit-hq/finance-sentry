import {CATEGORICAL_STEPS} from '@lifekit-hq/charts-core';
import {afterEach, describe, expect, it} from 'vitest';

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
    const colors = CATEGORICAL_STEPS.map((_, i) => ChartColorUtils.categorical(i));
    expect(new Set(colors).size).toBe(CATEGORICAL_STEPS.length);
  });

  it('wraps after the last categorical step', () => {
    expect(ChartColorUtils.categorical(CATEGORICAL_STEPS.length)).toBe(
      ChartColorUtils.categorical(0)
    );
  });

  it('never hands out the slate tail colour', () => {
    expect(CATEGORICAL_STEPS.map((_, i) => ChartColorUtils.categorical(i))).not.toContain(
      'var(--color-chart-series-9)'
    );
  });

  it('maps a negative index into the cycle', () => {
    expect(ChartColorUtils.categorical(-1)).toBe(
      ChartColorUtils.categorical(CATEGORICAL_STEPS.length - 1)
    );
  });
});

describe('ChartColorUtils fixed colours', () => {
  afterEach(() => {
    document.documentElement.style.removeProperty('--color-asset-crypto');
  });

  it('returns the fixed token of a fixed colour as a CSS colour', () => {
    expect(ChartColorUtils.css({fixed: 'asset-crypto'})).toBe('var(--color-asset-crypto)');
  });

  it('returns the series token of a step colour as a CSS colour', () => {
    expect(ChartColorUtils.css({step: 9})).toBe('var(--color-chart-series-9)');
  });

  it('resolves a fixed colour to the token value for a canvas', () => {
    document.documentElement.style.setProperty('--color-asset-crypto', 'rgb(186, 118, 0)');
    expect(ChartColorUtils.canvas({fixed: 'asset-crypto'})).toBe('rgb(186, 118, 0)');
  });
});
