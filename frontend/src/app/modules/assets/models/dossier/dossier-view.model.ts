import {type AreaSeries, type Nullable} from '@lifekit-hq/ui';

import {
  type ChartRangeId,
  type DossierCheckAxisDto,
  type DossierCheckAxisKey,
} from './dossier.model';

/** A timeframe chip; lk-chip-row reads `{label, value}`. */
export interface ChartRangeOption {
  label: string;
  value: ChartRangeId;
}

/** A buy or sell placed along the chart's time axis; `leftPct` is its x position, 0-100. */
export interface ChartTradeTick {
  leftPct: number;
  side: 'buy' | 'sell';
  title: string;
}

/**
 * The price chart as drawn: index-aligned lines (price, S&P 500, average cost) and the trade ticks.
 * The chart's own legend names the lines; `averageCostLabel` adds the cost it cannot show.
 */
export interface DossierChartView {
  series: AreaSeries[];
  averageCostLabel: Nullable<string>;
  ticks: ChartTradeTick[];
}

/** One leg of the return decomposition; `value` null = the ledger cannot state it. */
export interface ReturnLegView {
  key: 'unrealized' | 'realized' | 'dividends' | 'currency';
  label: string;
  value: Nullable<number>;
  /** Bar length against the largest leg, 0-100. */
  barPct: number;
}

export interface DossierReturnsView {
  legs: ReturnLegView[];
  total: Nullable<number>;
  note: string;
}

/** Size against the IPS cap: the bar scales so the cap mark sits inside it. */
export interface SizeCapView {
  sharePct: number;
  capPct: Nullable<number>;
  fillPct: number;
  capMarkPct: Nullable<number>;
  overCap: boolean;
}

/** A marker on the "Is it cheap?" bar; `leftPct` 0-100 along the scale. */
export interface TargetMarker {
  key: 'price' | 'analyst' | 'mine';
  label: string;
  value: number;
  leftPct: number;
}

/** Three bands: below the analysts' low, inside their range, above their high. */
export interface TargetBandView {
  currency: string;
  /** The analysts' range; null when only the price and the user's own target are known. */
  low: Nullable<number>;
  high: Nullable<number>;
  lowPct: Nullable<number>;
  highPct: Nullable<number>;
  markers: TargetMarker[];
  /** "Yahoo Finance · 38 analysts"; null without an analyst target. */
  source: Nullable<string>;
}

export interface WheelAxisView {
  key: DossierCheckAxisKey;
  label: string;
  /** Spoke end and label anchor in SVG units. */
  x: number;
  y: number;
  labelX: number;
  labelY: number;
  anchor: 'start' | 'middle' | 'end';
}

/** The summary wheel's geometry: spokes, rings and the filled shape, all as SVG points. */
export interface WheelView {
  size: number;
  centre: number;
  axes: WheelAxisView[];
  rings: string[];
  shape: string;
}

export type RuleState = 'pass' | 'fail' | 'unknown';

export interface CheckRuleView {
  key: string;
  label: string;
  state: RuleState;
  /** "28.5x vs 25.3x", "12.2% vs 10%": the fact and its bar; null when there is none. */
  detail: Nullable<string>;
  note: Nullable<string>;
}

export interface CheckAxisView extends Pick<DossierCheckAxisDto, 'key' | 'label' | 'passed'> {
  total: number;
  evaluated: number;
  rules: CheckRuleView[];
}

/** One cell of the fundamentals grid: a value, or the reason it has none. */
export interface FundamentalCell {
  key: string;
  label: string;
  value: Nullable<string>;
  reason: Nullable<string>;
  tone: Nullable<'gain' | 'loss'>;
}

/** One fact of the #824 performance strip ("year +22.2%"). */
export interface PerformanceStripItem {
  key: string;
  label: string;
  value: string;
  tone: Nullable<'gain' | 'loss'>;
}

/** One row of the Data Status footer: where a section came from and when. */
export interface DataStatusRow {
  key: string;
  label: string;
  /** Provider names; null when there is no source at all (the note says why). */
  source: Nullable<string>;
  /** ISO instant the data was last read; null when the source carries none. */
  at: Nullable<string>;
  /** Shown instead of a time: why the section is empty or partial. */
  note: Nullable<string>;
}
