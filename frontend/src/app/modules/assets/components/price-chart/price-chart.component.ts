import {ChangeDetectionStrategy, Component, CUSTOM_ELEMENTS_SCHEMA, inject} from '@angular/core';
import {type ChipRowChangeDetail, type SegmentedChangeDetail} from '@lifekit-hq/elements';
import {AsyncStateComponent, ButtonComponent, CardComponent} from '@lifekit-hq/ui';

import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';
import {DOSSIER_SECTION_ID} from '../../constants/dossier/dossier-sections.constants';
import {
  INTERVAL_REASON,
  MEASURE_HINT,
  MEASURE_OPTIONS,
  NO_HISTORY_MESSAGE,
  PRICE_CHART_SKELETON_HEIGHT,
  PRICE_KINDS,
  PRICE_PERIODS,
} from '../../constants/price-chart/price-chart.constants';
import {
  type MovingAverageId,
  type PriceChartKind,
  type PriceIntervalId,
  type PricePeriodId,
  type TimeSpan,
} from '../../models/price-chart/price-chart.model';
import {PriceChartStore} from '../../store/price-chart.store';
import {PriceChartCanvasComponent} from '../price-chart-canvas/price-chart-canvas.component';

/**
 * The dossier's interactive price chart: candles, line or area from the app's own price source, with the
 * periods and candle sizes that source can serve, moving-average overlays (the selection is remembered), the
 * 52-week range, a volume pane, a live change-over-view readout and a measure tool.
 */
@Component({
  selector: 'fns-price-chart',
  imports: [AsyncStateComponent, ButtonComponent, CardComponent, PriceChartCanvasComponent],
  providers: [PriceChartStore],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './price-chart.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceChartComponent {
  protected readonly store = inject(PriceChartStore);

  protected readonly sectionId = DOSSIER_SECTION_ID;
  protected readonly periods = PRICE_PERIODS;
  protected readonly kinds = PRICE_KINDS;
  protected readonly measureOptions = MEASURE_OPTIONS;
  protected readonly intervalReason = INTERVAL_REASON;
  protected readonly measureHint = MEASURE_HINT;
  protected readonly noHistoryMessage = NO_HISTORY_MESSAGE;
  protected readonly skeletonHeight = PRICE_CHART_SKELETON_HEIGHT;
  protected readonly gainClass = GAIN_TEXT_CLASS;
  protected readonly lossClass = LOSS_TEXT_CLASS;

  protected swatch(colour: Parameters<typeof ChartColorUtils.css>[0]): string {
    return ChartColorUtils.css(colour);
  }

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

  protected onVisibleChange(span: TimeSpan): void {
    this.store.setVisible(span);
  }
}
