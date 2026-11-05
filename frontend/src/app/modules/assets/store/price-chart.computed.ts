import {computed, type Signal} from '@angular/core';
import {type AsyncStateStatus, type Nullable} from '@lifekit-hq/ui';

import {
  AVERAGE_HINT,
  MOVING_AVERAGES,
  PRICE_INTERVALS,
} from '../constants/price-chart/price-chart.constants';
import {
  type AssetPriceBarsDto,
  type MovingAverageId,
  type PriceBarDto,
  type TimeSpan,
} from '../models/price-chart/price-chart.model';
import {MovingAverageUtils} from '../utils/moving-average.utils';
import {PriceChartViewUtils} from '../utils/price-chart-view.utils';
import {PriceRangeUtils} from '../utils/price-range.utils';

interface StateSignals {
  bars: Signal<Nullable<AssetPriceBarsDto>>;
  movingAverages: Signal<MovingAverageId[]>;
  visible: Signal<Nullable<TimeSpan>>;
  measured: Signal<Nullable<TimeSpan>>;
  hoverTime: Signal<Nullable<number>>;
  isBarsLoading: Signal<boolean>;
  hasBarsError: Signal<boolean>;
  barsErrorMessage: Signal<string>;
}

export function priceChartComputed(store: StateSignals) {
  const series = computed((): PriceBarDto[] => store.bars()?.bars ?? []);

  // A reload (new period, new candle size) keeps the chart on screen under a dimmed state, so the card
  // never collapses to a skeleton and back; only the first load, or a failed one, replaces it.
  const asyncStatus = computed((): AsyncStateStatus => {
    if (store.hasBarsError() && !store.isBarsLoading()) {
      return 'error';
    }
    return store.bars() === null ? 'loading' : 'success';
  });

  const lastBar = computed((): Nullable<PriceBarDto> => series().at(-1) ?? null);

  // The bar the legend reads: the one under the crosshair, else the last.
  const legendBar = computed((): Nullable<PriceBarDto> => {
    const hover = store.hoverTime();
    return (hover !== null ? series().find(b => b.time === hover) : null) ?? lastBar();
  });

  // The move across what is in view: recomputed on every pan and zoom, from the window until then.
  const visibleChange = computed(() => {
    const bars = store.bars();
    const span = store.visible() ?? (bars ? {from: bars.windowStart, to: Infinity} : null);
    return PriceRangeUtils.change(series(), span);
  });

  const measureChange = computed(() => PriceRangeUtils.change(series(), store.measured()));

  const gaps = computed(() => MovingAverageUtils.gaps(series(), store.movingAverages()));

  return {
    asyncStatus,
    isReloading: computed(() => store.isBarsLoading() && store.bars() !== null),
    isEmpty: computed(() => store.bars() !== null && series().length === 0),
    lines: computed(() => MovingAverageUtils.lines(series(), store.movingAverages())),
    gaps,
    unavailableAverages: computed(() => MovingAverageUtils.unavailable(series())),
    intervalOptions: computed(() => {
      const allowed = store.bars()?.intervals ?? [];
      return PRICE_INTERVALS.map(option => ({
        ...option,
        disabled: !allowed.includes(option.value),
      }));
    }),
    legendBar,
    visibleChange,
    measureChange,
    visibleReadout: computed(() =>
      PriceChartViewUtils.readout(visibleChange(), store.bars()?.currency ?? null)
    ),
    measureReadout: computed(() =>
      PriceChartViewUtils.readout(measureChange(), store.bars()?.currency ?? null)
    ),
    averageOptions: computed(() => {
      const unavailable = store.bars() === null ? [] : MovingAverageUtils.unavailable(series());
      return MOVING_AVERAGES.map(spec => ({
        value: spec.id,
        label: spec.label,
        disabled: unavailable.includes(spec.id) && !store.movingAverages().includes(spec.id),
      }));
    }),
    averageHint: computed(() =>
      store.bars() !== null && MovingAverageUtils.unavailable(series()).length > 0
        ? AVERAGE_HINT
        : null
    ),
    summary: computed(() =>
      PriceChartViewUtils.summary(store.bars(), PriceRangeUtils.change(series(), store.visible()))
    ),
    legend: computed(() => {
      const bars = store.bars();
      const bar = legendBar();
      return bars && bar ? PriceChartViewUtils.legend(bar, bars) : null;
    }),
    weekRangeText: computed(() => PriceChartViewUtils.weekRangeText(store.bars())),
    truncatedNote: computed(() => PriceChartViewUtils.truncatedNote(store.bars())),
    gapNote: computed(() => PriceChartViewUtils.gapNote(gaps())),
  };
}
