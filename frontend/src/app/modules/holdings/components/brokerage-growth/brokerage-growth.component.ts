import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AsyncStateComponent,
  DisclosureRowComponent,
  ListItemRowComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {BrokerageGrowthStore} from '../../store/brokerage-growth/brokerage-growth.store';
import {BookIncomeComponent} from '../book-income/book-income.component';
import {BookVsSpyComponent} from '../book-vs-spy/book-vs-spy.component';
import {BrokeragePnlComponent} from '../brokerage-pnl/brokerage-pnl.component';
import {CostOfInvestingComponent} from '../cost-of-investing/cost-of-investing.component';
import {RiskPictureComponent} from '../risk-picture/risk-picture.component';

/**
 * One performance panel per sleeve, as plain rows like the account rows above it: value now, contributed,
 * growth with its money-weighted return (finance-framework M2, M4), from the day the daily value history
 * starts, and for the brokerage the capital in the window with its realized and unrealized P&L (issue 982) and the
 * book against SPY and QQQ. While a provider has never reported a
 * deposit the split is withheld and the panel says so, instead of reading every deposit as growth.
 */
@Component({
  selector: 'fns-brokerage-growth',
  imports: [
    AsyncStateComponent,
    BookVsSpyComponent,
    RiskPictureComponent,
    BookIncomeComponent,
    BrokeragePnlComponent,
    CostOfInvestingComponent,
    DatePipe,
    DisclosureRowComponent,
    HeadlineBadgeComponent,
    ListItemRowComponent,
    MoneyPipe,
    SkeletonComponent,
  ],
  templateUrl: './brokerage-growth.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BrokerageGrowthStore],
})
export class BrokerageGrowthComponent {
  public readonly store = inject(BrokerageGrowthStore);
}
