import {describe, expect, it} from 'vitest';

import {
  type AssetPriceBarsDto,
  type PriceBarDto,
  type RangeChange,
} from '../models/price-chart/price-chart.model';
import {PriceChartViewUtils} from './price-chart-view.utils';

const START = 1_760_000_000; // 2025-10-09 08:53:20 on the exchange clock
const DAY = 86_400;

const bar = (time: number, close: number, volume = 0): PriceBarDto => ({
  time,
  open: close,
  high: close,
  low: close,
  close,
  volume,
});

const dto = (overrides: Partial<AssetPriceBarsDto> = {}): AssetPriceBarsDto => ({
  symbol: 'AAPL',
  period: '1Y',
  interval: '1d',
  intervals: ['1d', '1w'],
  currency: 'USD',
  timeZone: null,
  bars: [bar(START, 100), bar(START + DAY, 110)],
  windowStart: START,
  truncated: false,
  weekRange: {high: 120, low: 80, from: '2025-10-09', coversYear: true},
  source: 'yahoo:chart',
  retrievedAt: '2026-11-05T10:00:00Z',
  ...overrides,
});

const change = (value: number, pct: number | null, seconds: number): RangeChange => ({
  fromTime: START,
  toTime: START + seconds,
  fromPrice: 100,
  toPrice: 100 + value,
  change: value,
  changePct: pct,
  seconds,
  bars: 2,
});

describe('PriceChartViewUtils', () => {
  describe('move', () => {
    it('signs the amount and percent and names the span', () => {
      expect(PriceChartViewUtils.move(change(12.4, 7, 120 * DAY), 'USD')).toBe(
        '+$12.40 (+7.0%) over 120 days'
      );
    });

    it('leaves the percent out when there is no base', () => {
      expect(PriceChartViewUtils.move(change(1, null, DAY), 'USD')).toBe('+$1.00 over 1 day');
    });
  });

  describe('readout', () => {
    it('is a gain at zero and a loss below it', () => {
      expect(PriceChartViewUtils.readout(change(0, 0, DAY), 'USD')?.isGain).toBe(true);
      expect(PriceChartViewUtils.readout(change(-1, -1, DAY), 'USD')?.isGain).toBe(false);
    });

    it('is null with no change', () => {
      expect(PriceChartViewUtils.readout(null, 'USD')).toBeNull();
    });
  });

  describe('summary', () => {
    it('says there is no history for an empty chart', () => {
      expect(PriceChartViewUtils.summary(dto({bars: []}), null)).toBe(
        'Price chart: no price history.'
      );
      expect(PriceChartViewUtils.summary(null, null)).toBe('Price chart: no price history.');
    });

    it('names the symbol, period, candle size, last close and the move in view', () => {
      expect(PriceChartViewUtils.summary(dto(), change(10, 10, DAY))).toBe(
        'AAPL price chart, 1Y in Day candles. Last close $110.00. In view: +$10.00 (+10.0%) over 1 day.'
      );
    });
  });

  describe('precision', () => {
    it('gives a sub-dollar price four places and anything else two', () => {
      expect(PriceChartViewUtils.precision([{low: 0.0123}, {low: 0.5}])).toBe(4);
      expect(PriceChartViewUtils.precision([{low: 19.5}])).toBe(2);
    });
  });

  describe('volume', () => {
    it('keeps a small figure whole and compacts a large one', () => {
      expect(PriceChartViewUtils.volume(812)).toBe('812');
      expect(PriceChartViewUtils.volume(34_500)).toBe('34.5K');
      expect(PriceChartViewUtils.volume(1_200_000)).toBe('1.2M');
    });
  });

  describe('timeLabel', () => {
    it('reads the exchange clock as written: a date for a day, the time under one', () => {
      // 2026-10-12 13:30 on the exchange clock.
      const time = Date.UTC(2026, 9, 12, 13, 30) / 1000;
      expect(PriceChartViewUtils.timeLabel(time, '1d')).toBe('12 Oct 2026');
      expect(PriceChartViewUtils.timeLabel(time, '5m')).toBe('12 Oct 2026, 13:30');
    });
  });

  describe('legend', () => {
    it('formats OHLC and leaves volume out when the bar traded none', () => {
      const legend = PriceChartViewUtils.legend(bar(START, 100), dto());
      expect(legend).toMatchObject({open: '$100.00', close: '$100.00', volume: null});
      expect(PriceChartViewUtils.legend(bar(START, 100, 5_000), dto()).volume).toBe('5K');
    });
  });

  describe('weekRangeText', () => {
    it('states the 52-week range', () => {
      expect(PriceChartViewUtils.weekRangeText(dto())).toBe('52-week range $80.00 - $120.00');
    });

    it('says since when for a series younger than a year', () => {
      const text = PriceChartViewUtils.weekRangeText(
        dto({weekRange: {high: 120, low: 80, from: '2026-09-01', coversYear: false}})
      );
      // en-GB spells September "Sept" in newer CLDR data, which the full suite has registered.
      expect(text).toMatch(
        /^Range since 1 Sept? 2026 \$80\.00 - \$120\.00 \(less than 52 weeks of history\)$/
      );
    });

    it('is null with no range', () => {
      expect(PriceChartViewUtils.weekRangeText(dto({weekRange: null}))).toBeNull();
      expect(PriceChartViewUtils.weekRangeText(null)).toBeNull();
    });
  });

  describe('truncatedNote', () => {
    it('says where a short history starts', () => {
      expect(PriceChartViewUtils.truncatedNote(dto({truncated: true}))).toContain(
        'Price history starts'
      );
    });

    it('is null for a full period, an empty chart and no chart', () => {
      expect(PriceChartViewUtils.truncatedNote(dto())).toBeNull();
      expect(PriceChartViewUtils.truncatedNote(dto({truncated: true, bars: []}))).toBeNull();
      expect(PriceChartViewUtils.truncatedNote(null)).toBeNull();
    });
  });

  describe('gapNote', () => {
    it('names each average that needs more bars than there are', () => {
      expect(
        PriceChartViewUtils.gapNote([
          {id: 'sma200', needed: 200, available: 63},
          {id: 'sma100', needed: 100, available: 63},
        ])
      ).toBe(
        'Not drawn: SMA 200 needs 200 bars (63 available); SMA 100 needs 100 bars (63 available).'
      );
    });

    it('is null with no gaps', () => {
      expect(PriceChartViewUtils.gapNote([])).toBeNull();
    });
  });
});
