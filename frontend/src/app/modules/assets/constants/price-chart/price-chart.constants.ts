import {type ChartColour} from '../../../../shared/models/chart-colour/chart-colour.model';
import {
  type MovingAverageId,
  type MovingAverageSpec,
  type PriceChartKind,
  type PriceChartOption,
  type PriceChartSelection,
  type PriceIntervalId,
  type PricePeriodId,
} from '../../models/price-chart/price-chart.model';

export const PRICE_PERIODS: readonly PriceChartOption<PricePeriodId>[] = [
  {value: '1D', label: '1D'},
  {value: '5D', label: '5D'},
  {value: '1M', label: '1M'},
  {value: '3M', label: '3M'},
  {value: '6M', label: '6M'},
  {value: 'YTD', label: 'YTD'},
  {value: '1Y', label: '1Y'},
  {value: '5Y', label: '5Y'},
  {value: 'ALL', label: 'All'},
];

/** The periods the phone's one segmented row carries (a segmented control holds five at most); the rest live in Chart options. */
export const PRICE_PHONE_PERIODS: readonly PriceChartOption<PricePeriodId>[] = PRICE_PERIODS.filter(
  option => ['1D', '1M', '1Y', '5Y', 'ALL'].includes(option.value)
);

export const DEFAULT_PRICE_PERIOD: PricePeriodId = '1Y';

export const PRICE_INTERVALS: readonly PriceChartOption<PriceIntervalId>[] = [
  {value: '5m', label: '5m'},
  {value: '15m', label: '15m'},
  {value: '1h', label: '1h'},
  {value: '1d', label: 'Day'},
  {value: '1w', label: 'Week'},
  {value: '1mo', label: 'Month'},
];

export const PRICE_KINDS: readonly PriceChartOption<PriceChartKind>[] = [
  {value: 'candles', label: 'Candles'},
  {value: 'line', label: 'Line'},
  {value: 'area', label: 'Area'},
];

export const DEFAULT_PRICE_KIND: PriceChartKind = 'candles';

/** Series steps off the ramp's accent (the price line) and clear of gain and loss, which own green and red. */
export const MOVING_AVERAGES: readonly MovingAverageSpec[] = [
  {id: 'sma20', kind: 'sma', length: 20, label: 'SMA 20', colour: {step: 6}},
  {id: 'sma50', kind: 'sma', length: 50, label: 'SMA 50', colour: {step: 5}},
  {id: 'sma100', kind: 'sma', length: 100, label: 'SMA 100', colour: {step: 7}},
  {id: 'sma200', kind: 'sma', length: 200, label: 'SMA 200', colour: {step: 3}},
  {id: 'ema9', kind: 'ema', length: 9, label: 'EMA 9', colour: {step: 2}},
  {id: 'ema21', kind: 'ema', length: 21, label: 'EMA 21', colour: {step: 8}},
];

export const MOVING_AVERAGE_IDS: readonly MovingAverageId[] = MOVING_AVERAGES.map(m => m.id);

export const DEFAULT_PRICE_SELECTION: PriceChartSelection = {
  kind: DEFAULT_PRICE_KIND,
  movingAverages: [],
};

/** The price line and area lead the ramp, like the dossier's own chart. */
export const PRICE_LINE_COLOUR: ChartColour = {step: 1};
export const WEEK_RANGE_COLOUR: ChartColour = {step: 9};

/** Per-user, per-device: the chart kind and the moving averages left on. */
export const PRICE_CHART_STORAGE_PREFIX = 'fns.price-chart.v1.';
export const PRICE_CHART_ANONYMOUS_KEY = 'anonymous';

export const PRICE_CHART_HEIGHT_PX = 360;
export const PRICE_CHART_SKELETON_HEIGHT = '26rem';
export const VOLUME_PANE_STRETCH = 1;
export const PRICE_PANE_STRETCH = 4;
export const VOLUME_ALPHA = 0.5;
export const SELECTION_ALPHA = 0.14;
/** Bars of padding right of the last bar so its price tag clears the edge. */
export const RIGHT_OFFSET_BARS = 4;

export const SECONDS_PER_DAY = 86_400;
export const SECONDS_PER_HOUR = 3600;
export const SECONDS_PER_MINUTE = 60;

export const MEASURE_HINT =
  'To measure a span, hold Shift and drag across the chart, or turn on Measure and drag.';
/** The phone's version of the hint: no keyboard, so Measure is switched on in Chart options first. */
export const MEASURE_HINT_TOUCH =
  'To measure a span, turn on Measure in Chart options, then drag across the chart.';
export const CHART_OPTIONS_LABEL = 'Chart options';
export const MEASURE_OPTIONS: readonly PriceChartOption<'measure'>[] = [
  {value: 'measure', label: 'Measure'},
];
export const AVERAGE_HINT =
  'Greyed-out averages need more bars than this period and candle size load.';

export const INTERVAL_REASON =
  'Finer candles exist only for shorter periods: the price source keeps minute bars for the recent past only.';
export const NO_HISTORY_MESSAGE = 'No price history for this asset yet.';
