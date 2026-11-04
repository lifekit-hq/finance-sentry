import {ChangeDetectionStrategy, Component, computed, inject, signal} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {type Params, Router, RouterLink} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  MenuComponent,
  type MenuItem,
  MonthStepperComponent,
  PageContainerComponent,
  PageScrollComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {CategoryColourUtils} from '../../../../shared/utils/category-colour.utils';
import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';
import {DateUtils} from '../../../../shared/utils/date.utils';
import {
  BUDGET_NEAR_LIMIT_PCT,
  BUDGETS_LOAD_ERROR_MESSAGE,
} from '../../constants/budget/budget.constants';
import {type BudgetSummaryItem} from '../../models/budget/budget.model';
import {BudgetsStore} from '../../store/budgets/budgets.store';
import {BudgetEstimateUtils} from '../../utils/budget-estimate.utils';

const PCT_MAX = 100;
const SKELETON_CARDS = 3;

const EDIT_ITEM: MenuItem = {id: 'edit', label: 'Edit estimate', icon: 'Pencil'};
const TRANSACTIONS_ITEM: MenuItem = {
  id: 'transactions',
  label: 'View transactions',
  icon: 'ArrowRight',
};
const RESET_ITEM: MenuItem = {id: 'reset', label: 'Reset to suggested', icon: 'Undo2'};
const REMOVE_ITEM: MenuItem = {
  id: 'reset',
  label: 'Remove my amount',
  icon: 'Trash2',
  destructive: true,
};

const PLAIN_MENU: MenuItem[] = [EDIT_ITEM, TRANSACTIONS_ITEM];
const RESET_MENU: MenuItem[] = [EDIT_ITEM, RESET_ITEM, TRANSACTIONS_ITEM];
const REMOVE_MENU: MenuItem[] = [EDIT_ITEM, REMOVE_ITEM, TRANSACTIONS_ITEM];

const withoutTransactions = (menu: MenuItem[]): MenuItem[] =>
  menu.filter(entry => entry !== TRANSACTIONS_ITEM);
const PLAIN_ROLE_MENU = withoutTransactions(PLAIN_MENU);
const RESET_ROLE_MENU = withoutTransactions(RESET_MENU);
const REMOVE_ROLE_MENU = withoutTransactions(REMOVE_MENU);

@Component({
  selector: 'fns-budgets',
  imports: [
    PageContainerComponent,
    PageScrollComponent,
    InputHintsDirective,
    RouterLink,
    AlertComponent,
    AsyncStateComponent,
    CardComponent,
    FormsModule,
    IconComponent,
    InputComponent,
    MenuComponent,
    MoneyPipe,
    MonthStepperComponent,
    SkeletonComponent,
    TagComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BudgetsStore],
  templateUrl: './budgets.component.html',
})
export class BudgetsComponent {
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(BudgetsStore);
  public readonly transactionsRoute = AppRoute.Transactions;
  public readonly skeletonCards = Array.from({length: SKELETON_CARDS});
  // The cards belong to one month, so a failed or pending month never keeps the previous
  // month's cards on screen (`hasData: false`): it shows the skeleton or the error instead.
  // A failed refresh after an edit is the exception: the page is still valid, so it stays under
  // the error banner.
  public readonly view = computed(() => {
    const hasData = this.store.refreshFailed();
    return AsyncViewUtils.resolve({
      isLoading: this.store.status() === 'loading',
      hasData,
      errorMessage: hasData || this.store.status() === 'error' ? BUDGETS_LOAD_ERROR_MESSAGE : '',
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
  public readonly errorPlacement = computed(() =>
    this.store.refreshFailed() ? 'above' : 'replace'
  );
  // An empty month is either "not enough history yet" (nothing can be estimated) or a quiet month.
  public readonly emptyMessage = computed(() =>
    this.store.hasEnoughHistory()
      ? 'Nothing to estimate for ' + this.store.monthName()
      : 'Not enough history to estimate yet'
  );
  public readonly emptySubMessage = computed(() =>
    this.store.hasEnoughHistory()
      ? 'No category has a typical amount or any spending this month.'
      : `Estimates are learned from at least ${this.store.minHistoryMonths()} months of bank history; ${this.store.historyMonthsUsed()} so far.`
  );

  public readonly editValue = signal('');
  public readonly nearLimitPct = BUDGET_NEAR_LIMIT_PCT;
  public readonly currentMonth = new Date();

  public categoryColor(category: string): string {
    return ChartColorUtils.css(CategoryColourUtils.of(category));
  }

  public barPct(spent: number, estimate: number): number {
    return BudgetEstimateUtils.usedPct(spent, estimate);
  }

  public pacePct(paceRatio: number): number {
    return Math.round(paceRatio * PCT_MAX);
  }

  public barColor(spent: number, estimate: number, category: string): string {
    if (spent > estimate) {
      return 'var(--color-status-error)';
    }
    if (this.barPct(spent, estimate) >= BUDGET_NEAR_LIMIT_PCT) {
      return 'var(--color-status-warning)';
    }
    return this.categoryColor(category);
  }

  public hasTransactions(category: string): boolean {
    return BudgetEstimateUtils.hasTransactionCategory(category);
  }

  public menuFor(item: BudgetSummaryItem): MenuItem[] {
    const hasTransactions = this.hasTransactions(item.category);
    if (item.overrideId === null) {
      return hasTransactions ? PLAIN_MENU : PLAIN_ROLE_MENU;
    }
    if (item.suggestedAmount === null) {
      return hasTransactions ? REMOVE_MENU : REMOVE_ROLE_MENU;
    }
    return hasTransactions ? RESET_MENU : RESET_ROLE_MENU;
  }

  public onBudgetAction(action: string, item: BudgetSummaryItem): void {
    if (action === 'edit') {
      this.startEdit(item.category, item.estimate);
    } else if (action === 'transactions') {
      this.viewTransactions(item.category);
    } else if (action === 'reset' && item.overrideId !== null) {
      this.store.resetOverride(item.overrideId);
    }
  }

  public startEdit(category: string, estimate: number | null): void {
    this.editValue.set(estimate === null ? '' : String(estimate));
    this.store.setEditing(category);
  }

  public saveEdit(item: BudgetSummaryItem): void {
    // Enter/Escape clear the editing state and the input's removal fires focusout; ignore that late save.
    if (this.store.editingCategory() !== item.category) {
      return;
    }
    const amount = parseFloat(this.editValue());
    if (!isNaN(amount) && amount > 0) {
      this.store.saveOverride({category: item.category, overrideId: item.overrideId, amount});
    }
    this.store.setEditing(null);
  }

  public cancelEdit(): void {
    this.store.setEditing(null);
  }

  public viewTransactions(category: string): void {
    void this.router.navigate([this.transactionsRoute], {
      queryParams: this.transactionsParams(category),
    });
  }

  public transactionsParams(category: string): Params {
    const year = this.store.selectedYear();
    const month = this.store.selectedMonth();
    const from = DateUtils.isoDateUtc(new Date(Date.UTC(year, month - 1, 1)));
    const to = DateUtils.isoDateUtc(new Date(Date.UTC(year, month, 0)));
    return {category, type: 'debit', from, to};
  }

  public onMonthChange(month: Date): void {
    this.store.navigateToPeriod({year: month.getFullYear(), month: month.getMonth() + 1});
  }
}
