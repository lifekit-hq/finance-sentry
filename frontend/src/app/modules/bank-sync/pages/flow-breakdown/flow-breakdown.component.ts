import {DatePipe, DecimalPipe} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {type ChipRowChangeDetail, type ChipRowOption} from '@lifekit-hq/elements';
import {
  AsyncStateComponent,
  type AsyncStateStatus,
  CardComponent,
  CmnDrawerService,
  EmptyStateComponent,
  ListItemRowComponent,
  MonthStepperComponent,
  PageContainerComponent,
  SkeletonComponent,
  StatCardComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {
  ALL_ACCOUNTS_LABEL,
  ALL_ACCOUNTS_VALUE,
} from '../../constants/account-filter/account-filter.constants';
import {type FlowBreakdownItem} from '../../models/flow-breakdown/flow-breakdown.model';
import {FlowBreakdownStore} from '../../store/flow-breakdown/flow-breakdown.store';
import {FlowBreakdownUtils} from '../../utils/flow-breakdown.utils';
import {MonthKeyUtils} from '../../utils/month-key.utils';

const SKELETON_ROWS = 8;
const DRAWER_WIDTH = '480px';

@Component({
  selector: 'fns-flow-breakdown',
  imports: [
    PageContainerComponent,
    AsyncStateComponent,
    CardComponent,
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
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  host: {class: 'block h-full'},
  providers: [FlowBreakdownStore],
})
export class FlowBreakdownComponent {
  private readonly router = inject(Router);
  private readonly drawer = inject(CmnDrawerService);

  public readonly store = inject(FlowBreakdownStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});

  public readonly breakdownStatus = computed<AsyncStateStatus>(() => {
    if (this.store.isLoading()) {
      return 'loading';
    }
    return this.store.errorMessage() ? 'error' : 'success';
  });
  public readonly currentMonth = new Date();
  public readonly monthDate = computed(() => MonthKeyUtils.toDate(this.store.month()));
  public readonly transactionsRoute = AppRoute.Transactions;

  public readonly accountOptions = computed((): ChipRowOption[] => [
    {value: ALL_ACCOUNTS_VALUE, label: ALL_ACCOUNTS_LABEL},
    ...this.store.accountChips().map(chip => ({value: chip.accountId, label: chip.label})),
  ]);
  public readonly selectedAccount = computed(() => [
    this.store.accountFilter() ?? ALL_ACCOUNTS_VALUE,
  ]);

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

  public counterpartyQuery(item: FlowBreakdownItem): Nullable<string> {
    return FlowBreakdownUtils.counterpartyQuery(item);
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

  public onAccountChange(event: Event): void {
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.setAccountFilter(value === ALL_ACCOUNTS_VALUE ? null : value);
  }

  private navigateToMonth(month: string): void {
    void this.router.navigate([], {queryParams: {month}, queryParamsHandling: 'merge'});
  }
}
