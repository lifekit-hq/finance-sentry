import {DatePipe, DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router} from '@angular/router';
import {
  AlertComponent,
  CardComponent,
  ChipComponent,
  EmptyStateComponent,
  ListItemRowComponent,
  MonthStepperComponent,
  SkeletonComponent,
  StatCardComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {FlowBreakdownStore} from '../../store/flow-breakdown/flow-breakdown.store';
import {MonthKeyUtils} from '../../utils/month-key.utils';

const SKELETON_ROWS = 8;

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

  public readonly store = inject(FlowBreakdownStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});

  public readonly currentMonth = new Date();
  public readonly monthDate = computed(() => MonthKeyUtils.toDate(this.store.month()));

  public onMonthChange(month: Date): void {
    this.navigateToMonth(MonthKeyUtils.fromDate(month));
  }

  public toggleAccount(accountId: string): void {
    this.store.setAccountFilter(accountId);
  }

  private navigateToMonth(month: string): void {
    void this.router.navigate([], {queryParams: {month}, queryParamsHandling: 'merge'});
  }
}
