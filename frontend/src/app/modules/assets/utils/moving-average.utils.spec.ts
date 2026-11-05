import {describe, expect, it} from 'vitest';

import {MOVING_AVERAGES} from '../constants/price-chart/price-chart.constants';
import {type MovingAverageSpec, type PriceBarDto} from '../models/price-chart/price-chart.model';
import {MovingAverageUtils} from './moving-average.utils';

const bar = (time: number, close: number): PriceBarDto => ({
  time,
  offset: 0,
  open: close,
  high: close,
  low: close,
  close,
  volume: 1,
});

const bars = (closes: number[]): PriceBarDto[] => closes.map((c, i) => bar(i * 100, c));

const spec = (id: string): MovingAverageSpec => {
  const found = MOVING_AVERAGES.find(m => m.id === id);
  if (!found) {
    throw new Error(`no such average: ${id}`);
  }
  return found;
};

describe('MovingAverageUtils', () => {
  describe('sma', () => {
    it('is null until a full window exists, then the rolling mean', () => {
      expect(MovingAverageUtils.sma([1, 2, 3, 4, 5], 3)).toEqual([null, null, 2, 3, 4]);
    });

    it('is all null when the series is shorter than the window', () => {
      expect(MovingAverageUtils.sma([1, 2], 3)).toEqual([null, null]);
    });

    it('handles a window of one', () => {
      expect(MovingAverageUtils.sma([4, 6], 1)).toEqual([4, 6]);
    });

    it('is empty for no values', () => {
      expect(MovingAverageUtils.sma([], 3)).toEqual([]);
    });
  });

  describe('ema', () => {
    it('seeds with the simple mean of the first window, then smooths with 2 / (n + 1)', () => {
      const [a, b, c, d] = MovingAverageUtils.ema([2, 4, 6, 8], 3);
      expect(a).toBeNull();
      expect(b).toBeNull();
      expect(c).toBe(4);
      // weight 0.5: 8 * 0.5 + 4 * 0.5
      expect(d).toBe(6);
    });

    it('is all null when the series is shorter than the seed', () => {
      expect(MovingAverageUtils.ema([1, 2], 3)).toEqual([null, null]);
    });

    it('tracks a flat series exactly', () => {
      const out = MovingAverageUtils.ema([5, 5, 5, 5, 5], 3);
      expect(out.slice(2)).toEqual([5, 5, 5]);
    });
  });

  describe('line', () => {
    it('keeps the time of each bar where the average is defined', () => {
      const line = MovingAverageUtils.line(bars([1, 2, 3, 4]), {...spec('sma20'), length: 2});
      expect(line.points).toEqual([
        {time: 100, value: 1.5},
        {time: 200, value: 2.5},
        {time: 300, value: 3.5},
      ]);
    });

    it('has no points when too few bars are loaded', () => {
      expect(MovingAverageUtils.line(bars([1, 2, 3]), spec('sma20')).points).toEqual([]);
    });
  });

  describe('lines', () => {
    it('returns only the averages switched on, in catalogue order', () => {
      const closes = Array.from({length: 60}, (_, i) => i + 1);
      const lines = MovingAverageUtils.lines(bars(closes), ['sma50', 'ema9']);
      expect(lines.map(l => l.spec.id)).toEqual(['sma50', 'ema9']);
      expect(lines[0].points).toHaveLength(11);
      expect(lines[1].points).toHaveLength(52);
    });

    it('is empty when none is on', () => {
      expect(MovingAverageUtils.lines(bars([1, 2, 3]), [])).toEqual([]);
    });
  });

  describe('gaps', () => {
    it('names an average on that needs more bars than are loaded', () => {
      const closes = Array.from({length: 120}, (_, i) => i + 1);
      expect(MovingAverageUtils.gaps(bars(closes), ['sma50', 'sma200'])).toEqual([
        {id: 'sma200', needed: 200, available: 120},
      ]);
    });

    it('is empty when nothing is on or every average fits', () => {
      expect(MovingAverageUtils.gaps(bars([1]), [])).toEqual([]);
      expect(MovingAverageUtils.gaps(bars(Array.from({length: 25}, () => 1)), ['sma20'])).toEqual(
        []
      );
    });
  });

  describe('unavailable', () => {
    it('lists every average the loaded bars are too few for', () => {
      expect(MovingAverageUtils.unavailable(bars(Array.from({length: 60}, () => 1)))).toEqual([
        'sma100',
        'sma200',
      ]);
    });
  });
});
