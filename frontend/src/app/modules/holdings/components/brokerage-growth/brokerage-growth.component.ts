import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AsyncStateComponent,
  CardComponent,
  SkeletonComponent,
  StatCardComponent,
} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {BrokerageGrowthStore} from '../../store/brokerage-growth/brokerage-growth.store';
import {BookVsSpyComponent} from '../book-vs-spy/book-vs-spy.component';

/**
 * One performance card per sleeve: contributed, growth with its money-weighted return (finance-framework M2,
 * M4) and value now, from the day the daily value history starts, and for the brokerage the book against SPY
 * on one line. While a provider has never reported a deposit the split is withheld and the card says so,
 * instead of reading every deposit as growth.
 */
@Component({
  selector: 'fns-brokerage-growth',
  imports: [
    AsyncStateComponent,
    BookVsSpyComponent,
    CardComponent,
    DatePipe,
    HeadlineBadgeComponent,
    MoneyPipe,
    SkeletonComponent,
    StatCardComponent,
  ],
  templateUrl: './brokerage-growth.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BrokerageGrowthStore],
})
export class BrokerageGrowthComponent {
  public readonly store = inject(BrokerageGrowthStore);
}
