import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {AsyncStateComponent, SkeletonComponent} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {BrokerageGrowthStore} from '../../store/brokerage-growth/brokerage-growth.store';

/**
 * The brokerage sleeve's contributions against its growth (finance-framework M2) with the money-weighted
 * return (M4), from the day the daily value history starts. While a provider has never reported a deposit
 * the split is withheld and the section says so, instead of reading every deposit as growth.
 */
@Component({
  selector: 'fns-brokerage-growth',
  imports: [AsyncStateComponent, DatePipe, HeadlineBadgeComponent, MoneyPipe, SkeletonComponent],
  templateUrl: './brokerage-growth.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BrokerageGrowthStore],
})
export class BrokerageGrowthComponent {
  public readonly store = inject(BrokerageGrowthStore);
}
