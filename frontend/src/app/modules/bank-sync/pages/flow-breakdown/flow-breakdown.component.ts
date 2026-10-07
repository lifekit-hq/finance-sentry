import {DatePipe, DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {
  AlertComponent,
  CardComponent,
  ChipComponent,
  CmnDrawerService,
  EmptyStateComponent,
  ListItemRowComponent,
  MonthStepperComponent,
  SkeletonComponent,
  StatCardComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {type FlowBreakdownItem} from '../../models/flow-breakdown/flow-breakdown.model';
import {FlowBreakdownStore} from '../../store/flow-breakdown/flow-breakdown.store';
import {FlowBreakdownUtils} from '../../utils/flow-breakdown.utils';
import {MonthKeyUtils} from '../../utils/month-key.utils';

const SKELETON_ROWS = 8;
const DRAWER_WIDTH = '480px';

@Component({
  selector: 'fns-flow-breakdown',
  imports: [
    AlertComponent,
    CardComponent,
    ChipComponent,
    DatePipe,
    DecimalPipe,
    EmptyStateComponent,
    ListItemRowComponent,
    MonthStepperComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    RouterLink,
    SkeletonComponent,
    StatCardComponent,
    TagComponent,
  ],
  templateUrl: './flow-breakdown.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block h-full'},
  providers: [FlowBreakdownStore],
})
export class FlowBreakdownComponent {
  private readonly router = inject(Router);
  private readonly drawer = inject(CmnDrawerService);

  public readonly store = inject(FlowBreakdownStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});

  public readonly currentMonth = new Date();
  public readonly monthDate = computed(() => MonthKeyUtils.toDate(this.store.month()));
  public readonly transactionsRoute = AppRoute.Transactions;

  // The days the page covers, so a drill-down lists the same window the figures total.
  public readonly windowDates = computed((): {from?: string; to?: string} => {
    const range = this.store.range();
    if (!range) {
      return FlowBreakdownUtils.monthDates(this.store.month());
    }
    return range.from ? {from: range.from, to: range.to} : {to: range.to};
  });

  public onMonthChange(month: Date): void {
    this.navigateToMonth(MonthKeyUtils.fromDate(month));
  }

  public ledgerParams(extra: Record<string, string> = {}): Record<string, string> {
    return {...extra, ...this.windowDates()};
  }

  public cardParams(type: 'credit' | 'debit'): Record<string, string> {
    const account = this.store.accountFilter();
    return this.ledgerParams(account ? {type, account} : {type});
  }

  public openTransaction(item: FlowBreakdownItem): void {
    this.drawer.open(TransactionDrawerComponent, {
      title: item.description,
      data: FlowBreakdownUtils.toTransaction(item),
      width: DRAWER_WIDTH,
    });
  }

  public toggleAccount(accountId: string): void {
    this.store.setAccountFilter(accountId);
  }

  private navigateToMonth(month: string): void {
    void this.router.navigate([], {queryParams: {month}, queryParamsHandling: 'merge'});
  }
}
