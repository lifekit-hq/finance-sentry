import {ChangeDetectionStrategy, Component, CUSTOM_ELEMENTS_SCHEMA, inject} from '@angular/core';
import {type ChipRowChangeDetail, type SegmentedChangeDetail} from '@lifekit-hq/elements';
import {CMN_DRAWER_DATA} from '@lifekit-hq/ui';

import {
  MEASURE_OPTIONS,
  PRICE_KINDS,
  PRICE_PERIODS,
} from '../../constants/price-chart/price-chart.constants';
import {
  type MovingAverageId,
  type PriceChartKind,
  type PriceChartOptionsSheetData,
  type PriceIntervalId,
  type PricePeriodId,
} from '../../models/price-chart/price-chart.model';

/**
 * The phone's Chart options sheet: every control the chart keeps off the phone screen (the full period list, candle
 * size, chart type, moving averages, Measure). It edits the chart's own store, so the chart behind it redraws as a
 * choice is made and there is nothing to apply.
 */
@Component({
  selector: 'fns-price-chart-options-sheet',
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './price-chart-options-sheet.component.html',
})
export class PriceChartOptionsSheetComponent {
  protected readonly store = inject<PriceChartOptionsSheetData>(CMN_DRAWER_DATA).store;

  protected readonly periods = PRICE_PERIODS;
  protected readonly kinds = PRICE_KINDS;
  protected readonly measureOptions = MEASURE_OPTIONS;

  protected onPeriodChange(event: Event): void {
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.selectPeriod(value as PricePeriodId);
  }

  protected onIntervalChange(event: Event): void {
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.selectInterval(value as PriceIntervalId);
  }

  protected onKindChange(event: Event): void {
    const {value} = (event as CustomEvent<SegmentedChangeDetail>).detail;
    this.store.selectKind(value as PriceChartKind);
  }

  protected onAverageChange(event: Event): void {
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.toggleMovingAverage(value as MovingAverageId);
  }

  protected onMeasureChange(event: Event): void {
    const {selected} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.setMeasuring(selected.includes('measure'));
  }
}
