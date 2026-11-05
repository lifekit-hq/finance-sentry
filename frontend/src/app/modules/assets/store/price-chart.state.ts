import {type Nullable} from '@lifekit-hq/ui';

import {
  DEFAULT_PRICE_PERIOD,
  DEFAULT_PRICE_SELECTION,
} from '../constants/price-chart/price-chart.constants';
import {
  type AssetPriceBarsDto,
  type MovingAverageId,
  type PriceChartKind,
  type PriceIntervalId,
  type PricePeriodId,
  type TimeSpan,
} from '../models/price-chart/price-chart.model';

export interface PriceChartState {
  symbol: Nullable<string>;
  period: PricePeriodId;
  /** Null = the candle size the period opens on; the response names the one it used. */
  interval: Nullable<PriceIntervalId>;
  kind: PriceChartKind;
  movingAverages: MovingAverageId[];
  bars: Nullable<AssetPriceBarsDto>;
  /** The bars in view after pan/zoom, as bar times. */
  visible: Nullable<TimeSpan>;
  /** The span picked with the measure tool; null when none. */
  measured: Nullable<TimeSpan>;
  isMeasuring: boolean;
  /** The bar under the crosshair; null = show the last bar. */
  hoverTime: Nullable<number>;
}

export const initialPriceChartState: PriceChartState = {
  symbol: null,
  period: DEFAULT_PRICE_PERIOD,
  interval: null,
  kind: DEFAULT_PRICE_SELECTION.kind,
  movingAverages: [...DEFAULT_PRICE_SELECTION.movingAverages],
  bars: null,
  visible: null,
  measured: null,
  isMeasuring: false,
  hoverTime: null,
};
