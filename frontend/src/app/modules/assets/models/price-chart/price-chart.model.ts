import {type Nullable} from '@lifekit-hq/ui';

import {type ChartColour} from '../../../../shared/models/chart-colour/chart-colour.model';

export type PricePeriodId = '1D' | '5D' | '1M' | '3M' | '6M' | 'YTD' | '1Y' | '5Y' | 'ALL';

export type PriceIntervalId = '5m' | '15m' | '1h' | '1d' | '1w' | '1mo';

export type PriceChartKind = 'candles' | 'line' | 'area';

export type MovingAverageId = 'sma20' | 'sma50' | 'sma100' | 'sma200' | 'ema9' | 'ema21';

/**
 * One bar; `time` is the bar's real start in epoch seconds (strictly ascending down a series), `offset` the
 * exchange's UTC offset in seconds at that bar: `time + offset` read as UTC is the exchange's own wall-clock.
 */
export interface PriceBarDto {
  time: number;
  offset: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

/** The 52 weeks to the last daily bar; `coversYear` false = the series is younger, so it runs from `from`. */
export interface WeekRangeDto {
  high: number;
  low: number;
  from: string;
  coversYear: boolean;
}

/**
 * GET /research/assets/{symbol}/bars: the period's window plus up to 200 bars before it (so a moving
 * average is defined from `windowStart`); `intervals` are the candle sizes this period can be drawn at.
 * An empty `bars` = no price history from the source.
 */
export interface AssetPriceBarsDto {
  symbol: string;
  period: PricePeriodId;
  interval: PriceIntervalId;
  intervals: PriceIntervalId[];
  currency: Nullable<string>;
  timeZone: Nullable<string>;
  bars: PriceBarDto[];
  windowStart: number;
  truncated: boolean;
  weekRange: Nullable<WeekRangeDto>;
  source: string;
  retrievedAt: string;
}

export interface MovingAverageSpec {
  id: MovingAverageId;
  kind: 'sma' | 'ema';
  length: number;
  label: string;
  colour: ChartColour;
}

export interface LinePoint {
  time: number;
  value: number;
}

export interface MovingAverageLine {
  spec: MovingAverageSpec;
  points: LinePoint[];
}

/** A moving average the loaded bars are too few to draw, with how many it needs. */
export interface MovingAverageGap {
  id: MovingAverageId;
  needed: number;
  available: number;
}

/** A span of bars, picked by pan/zoom or by the measure tool, as real bar times (inclusive). */
export interface TimeSpan {
  from: number;
  to: number;
}

/** The move across a span: first bar's close to last bar's close. */
export interface RangeChange {
  fromTime: number;
  toTime: number;
  fromPrice: number;
  toPrice: number;
  change: number;
  changePct: Nullable<number>;
  /** Real seconds between the two bars. */
  seconds: number;
  bars: number;
}

export interface PriceChartSelection {
  kind: PriceChartKind;
  movingAverages: MovingAverageId[];
}

export interface PriceChartOption<T extends string> {
  value: T;
  label: string;
}
