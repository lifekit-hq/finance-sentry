import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AlertComponent,
  AsyncStateComponent,
  CardComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {CASHFLOW_PROFILE_EMPTY_MESSAGE} from '../../constants/cashflow-profile/cashflow-profile.constants';
import {CashflowProfileStore} from '../../store/cashflow-profile/cashflow-profile.store';

const SKELETON_FIGURES = 3;

/**
 * Burn, savings rate and runway over the trailing complete months (#454 S1): the three figures with
 * the arithmetic behind them in words. Context for the person's own planning, never a forecast.
 */
@Component({
  selector: 'fns-cashflow-profile',
  imports: [AlertComponent, AsyncStateComponent, CardComponent, MoneyPipe, SkeletonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [CashflowProfileStore],
  templateUrl: './cashflow-profile.component.html',
})
export class CashflowProfileComponent {
  public readonly store = inject(CashflowProfileStore);
  public readonly emptyMessage = CASHFLOW_PROFILE_EMPTY_MESSAGE;
  public readonly skeletonFigures = Array.from({length: SKELETON_FIGURES});
}
