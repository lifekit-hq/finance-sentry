import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type AssetPriceBarsDto,
  type MovingAverageId,
  type PriceChartKind,
  type PriceIntervalId,
  type PricePeriodId,
  type TimeSpan,
} from '../models/price-chart/price-chart.model';
import {type PriceChartState} from './price-chart.state';

export function priceChartMethods(store: WritableStateSource<PriceChartState>) {
  return {
    // A symbol change in place drops the previous symbol's bars and any span picked on them; the period,
    // kind and averages are the reader's choices, not the symbol's.
    resetForSymbol(symbol: string): void {
      patchState(store, {
        symbol,
        bars: null,
        interval: null,
        visible: null,
        measured: null,
        isMeasuring: false,
        hoverTime: null,
      });
    },
    setPeriod(period: PricePeriodId): void {
      patchState(store, {period, interval: null, measured: null});
    },
    setInterval(interval: PriceIntervalId): void {
      patchState(store, {interval, measured: null});
    },
    setBars(bars: AssetPriceBarsDto): void {
      patchState(store, {bars, interval: bars.interval, visible: null, measured: null});
    },
    setKind(kind: PriceChartKind): void {
      patchState(store, {kind});
    },
    setMovingAverages(movingAverages: MovingAverageId[]): void {
      patchState(store, {movingAverages});
    },
    setVisible(visible: Nullable<TimeSpan>): void {
      patchState(store, {visible});
    },
    setMeasured(measured: Nullable<TimeSpan>): void {
      patchState(store, {measured});
    },
    setMeasuring(isMeasuring: boolean): void {
      patchState(store, state => ({isMeasuring, measured: isMeasuring ? state.measured : null}));
    },
    setHoverTime(hoverTime: Nullable<number>): void {
      patchState(store, {hoverTime});
    },
  };
}
