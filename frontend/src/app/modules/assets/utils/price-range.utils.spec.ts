import {describe, expect, it} from 'vitest';

import {type PriceBarDto} from '../models/price-chart/price-chart.model';
import {PriceRangeUtils} from './price-range.utils';

const DAY = 86_400;

const bars: PriceBarDto[] = [100, 110, 99, 120].map((close, i) => ({
  time: i * DAY,
  offset: 0,
  open: close,
  high: close,
  low: close,
  close,
  volume: 1,
}));

describe('PriceRangeUtils.change', () => {
  it('states the move from the first close to the last across the span', () => {
    const change = PriceRangeUtils.change(bars, {from: 0, to: 3 * DAY});
    expect(change).toMatchObject({
      fromPrice: 100,
      toPrice: 120,
      change: 20,
      changePct: 20,
      seconds: 3 * DAY,
      bars: 4,
    });
  });

  it('snaps to the bars inside the span', () => {
    const change = PriceRangeUtils.change(bars, {from: DAY - 5, to: 2 * DAY + 5});
    expect(change).toMatchObject({fromTime: DAY, toTime: 2 * DAY, change: -11, bars: 2});
  });

  it('reads a backwards span the same as a forwards one', () => {
    expect(PriceRangeUtils.change(bars, {from: 3 * DAY, to: 0})).toEqual(
      PriceRangeUtils.change(bars, {from: 0, to: 3 * DAY})
    );
  });

  it('gives a loss a negative change and percent', () => {
    const change = PriceRangeUtils.change(bars, {from: DAY, to: 2 * DAY});
    expect(change?.change).toBe(-11);
    expect(change?.changePct).toBeCloseTo(-10, 5);
  });

  it('is null across a single bar, with no span or no bars', () => {
    expect(PriceRangeUtils.change(bars, {from: DAY, to: DAY})).toBeNull();
    expect(PriceRangeUtils.change(bars, null)).toBeNull();
    expect(PriceRangeUtils.change([], {from: 0, to: DAY})).toBeNull();
  });

  it('is null when the span starts after the last bar', () => {
    expect(PriceRangeUtils.change(bars, {from: 10 * DAY, to: 11 * DAY})).toBeNull();
  });

  it('has no percent when the first close is zero', () => {
    const zero: PriceBarDto[] = [
      {time: 0, offset: 0, open: 0, high: 0, low: 0, close: 0, volume: 0},
      {time: DAY, offset: 0, open: 1, high: 1, low: 1, close: 1, volume: 0},
    ];
    expect(PriceRangeUtils.change(zero, {from: 0, to: DAY})?.changePct).toBeNull();
  });
});

describe('PriceRangeUtils.spanLabel', () => {
  it('counts days, singular for one', () => {
    expect(PriceRangeUtils.spanLabel(DAY)).toBe('1 day');
    expect(PriceRangeUtils.spanLabel(12 * DAY)).toBe('12 days');
  });

  it('reads hours and minutes under a day', () => {
    expect(PriceRangeUtils.spanLabel(3 * 3600 + 25 * 60)).toBe('3h 25m');
    expect(PriceRangeUtils.spanLabel(2 * 3600)).toBe('2h');
    expect(PriceRangeUtils.spanLabel(45 * 60)).toBe('45m');
  });
});
