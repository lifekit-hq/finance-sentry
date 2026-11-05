import {describe, expect, it} from 'vitest';

import {type PriceBarDto} from '../models/price-chart/price-chart.model';
import {PriceChartDataUtils} from './price-chart-data.utils';

const up: PriceBarDto = {time: 1, open: 10, high: 12, low: 9, close: 11, volume: 100};
const down: PriceBarDto = {time: 2, open: 11, high: 11, low: 8, close: 9, volume: 0};

describe('PriceChartDataUtils', () => {
  it('reshapes bars to candles without the volume', () => {
    expect(PriceChartDataUtils.candles([up])).toEqual([
      {time: 1, open: 10, high: 12, low: 9, close: 11},
    ]);
  });

  it('reshapes bars to a close line', () => {
    expect(PriceChartDataUtils.closes([up, down])).toEqual([
      {time: 1, value: 11},
      {time: 2, value: 9},
    ]);
  });

  it('tints volume gain where the bar closed at or above its open and loss below', () => {
    const volumes = PriceChartDataUtils.volumes([up, down], '#00673c', '#af1426');
    expect(volumes[0]).toEqual({time: 1, value: 100, color: 'rgba(0, 103, 60, 0.5)'});
    expect(volumes[1].color).toBe('rgba(175, 20, 38, 0.5)');
  });

  it('draws a flat bar as a gain', () => {
    const flat: PriceBarDto = {...up, close: 10};
    expect(PriceChartDataUtils.volumes([flat], '#00ff00', '#ff0000')[0].color).toBe(
      'rgba(0, 255, 0, 0.5)'
    );
  });

  it('reports whether any bar traded', () => {
    expect(PriceChartDataUtils.hasVolume([up, down])).toBe(true);
    expect(PriceChartDataUtils.hasVolume([down])).toBe(false);
    expect(PriceChartDataUtils.hasVolume([])).toBe(false);
  });
});
