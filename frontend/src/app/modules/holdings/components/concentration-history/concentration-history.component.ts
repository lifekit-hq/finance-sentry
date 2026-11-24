import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AsyncStateComponent,
  LineChartComponent,
  SegmentedComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {
  CONCENTRATION_BASIS,
  CONCENTRATION_MEASURES,
  CONCENTRATION_NO_DATA,
  CONCENTRATION_TOO_LITTLE_HISTORY,
} from '../../constants/risk-picture/concentration-history.constants';
import {ConcentrationHistoryStore} from '../../store/concentration-history/concentration-history.store';

/**
 * One small chart in the risk picture: the top-three share, the crypto share and the cash share of the book over the
 * days it was captured, one measure at a time, with the IPS target and the drift from it beneath. It reads figures
 * already stored; with fewer than two captured days it says so instead of drawing a line.
 */
@Component({
  selector: 'fns-concentration-history',
  imports: [AsyncStateComponent, LineChartComponent, SegmentedComponent, SkeletonComponent],
  templateUrl: './concentration-history.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [ConcentrationHistoryStore],
})
export class ConcentrationHistoryComponent {
  public readonly store = inject(ConcentrationHistoryStore);
  public readonly measures = CONCENTRATION_MEASURES;
  public readonly basis = CONCENTRATION_BASIS;
  public readonly tooLittleHistory = CONCENTRATION_TOO_LITTLE_HISTORY;
  public readonly noData = CONCENTRATION_NO_DATA;

  public chooseMeasure(value: string): void {
    const measure = CONCENTRATION_MEASURES.find(m => m.value === value);
    if (measure) {
      this.store.selectMeasure(measure.value);
    }
  }
}
