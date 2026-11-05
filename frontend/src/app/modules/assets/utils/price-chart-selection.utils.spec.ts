import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {PriceChartSelectionUtils} from './price-chart-selection.utils';

describe('PriceChartSelectionUtils', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('reads the defaults when nothing was stored', () => {
    expect(PriceChartSelectionUtils.read('u1')).toEqual({kind: 'candles', movingAverages: []});
  });

  it('round-trips the selection per user', () => {
    PriceChartSelectionUtils.write('u1', {kind: 'area', movingAverages: ['sma200']});
    PriceChartSelectionUtils.write('u2', {kind: 'line', movingAverages: ['ema9', 'sma50']});

    expect(PriceChartSelectionUtils.read('u1')).toEqual({kind: 'area', movingAverages: ['sma200']});
    expect(PriceChartSelectionUtils.read('u2')).toEqual({
      kind: 'line',
      movingAverages: ['sma50', 'ema9'],
    });
  });

  it('keys a signed-out visitor apart from any user', () => {
    PriceChartSelectionUtils.write(null, {kind: 'line', movingAverages: []});
    expect(PriceChartSelectionUtils.read('u1').kind).toBe('candles');
    expect(PriceChartSelectionUtils.read(null).kind).toBe('line');
  });

  it('drops unknown averages and kinds', () => {
    localStorage.setItem(
      PriceChartSelectionUtils.key('u1'),
      JSON.stringify({kind: 'renko', movingAverages: ['sma200', 'sma7', 3]})
    );
    expect(PriceChartSelectionUtils.read('u1')).toEqual({
      kind: 'candles',
      movingAverages: ['sma200'],
    });
  });

  it.each(['not json', '"str"', 'null', '[]'])('falls back on %s', raw => {
    localStorage.setItem(PriceChartSelectionUtils.key('u1'), raw);
    expect(PriceChartSelectionUtils.read('u1').movingAverages).toEqual([]);
  });

  it('falls back when storage cannot be read', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(PriceChartSelectionUtils.read('u1')).toEqual({kind: 'candles', movingAverages: []});
  });

  it('does not throw when storage cannot be written', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(() =>
      PriceChartSelectionUtils.write('u1', {kind: 'line', movingAverages: []})
    ).not.toThrow();
  });

  describe('toggle', () => {
    it('adds an average in catalogue order and removes it again', () => {
      const on = PriceChartSelectionUtils.toggle(['ema9'], 'sma20');
      expect(on).toEqual(['sma20', 'ema9']);
      expect(PriceChartSelectionUtils.toggle(on, 'ema9')).toEqual(['sma20']);
    });
  });
});
