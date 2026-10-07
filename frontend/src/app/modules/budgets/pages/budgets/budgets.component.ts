import {ChangeDetectionStrategy, Component, inject, signal, ViewContainerRef} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {Router} from '@angular/router';
import {
  AlertComponent,
  CardComponent,
  CmnDialogService,
  EmptyStateComponent,
  IconComponent,
  InputComponent,
  MenuComponent,
  type MenuItem,
  MonthStepperComponent,
  PageHeaderComponent,
  TagComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';
import {DateRangeUtils} from '../../../../shared/utils/date-range.utils';
import {AddBudgetDialogComponent} from '../../components/add-budget-dialog/add-budget-dialog.component';
import {BUDGET_NEAR_LIMIT_PCT} from '../../constants/budget/budget.constants';
import {type CreateBudgetRequest} from '../../models/budget/budget.model';
import {BudgetsStore} from '../../store/budgets/budgets.store';

const PCT_MAX = 100;

const BUDGET_MENU_ITEMS: MenuItem[] = [
  {id: 'edit', label: 'Edit limit', icon: 'Pencil'},
  {id: 'transactions', label: 'View transactions', icon: 'ArrowRight'},
  {id: 'remove', label: 'Remove budget', icon: 'Trash2', destructive: true},
];

@Component({
  selector: 'fns-budgets',
  imports: [
    InputHintsDirective,
    AlertComponent,
    CardComponent,
    EmptyStateComponent,
    FormsModule,
    IconComponent,
    InputComponent,
    MenuComponent,
    MoneyPipe,
    MonthStepperComponent,
    PageHeaderComponent,
    TagComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [BudgetsStore],
  templateUrl: './budgets.component.html',
})
export class BudgetsComponent {
  private readonly router = inject(Router);
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);

  public readonly store = inject(BudgetsStore);

  public readonly editValue = signal('');
  public readonly budgetMenuItems = BUDGET_MENU_ITEMS;
  public readonly nearLimitPct = BUDGET_NEAR_LIMIT_PCT;
  public readonly currentMonth = new Date();

  // By position in the grid, not by category: the colours repeat only every eight cards, so
  // cards that sit side by side never share one.
  public categoryColor(index: number): string {
    return ChartColorUtils.categorical(index);
  }

  public barPct(spent: number, limit: number): number {
    return Math.min((spent / limit) * PCT_MAX, PCT_MAX);
  }

  public pacePct(paceRatio: number): number {
    return Math.round(paceRatio * PCT_MAX);
  }

  public barColor(spent: number, limit: number, index: number): string {
    const pct = (spent / limit) * PCT_MAX;
    if (spent > limit) {
      return 'var(--color-status-error)';
    }
    if (pct >= BUDGET_NEAR_LIMIT_PCT) {
      return 'var(--color-status-warning)';
    }
    return this.categoryColor(index);
  }

  public onBudgetAction(
    action: string,
    budget: {id: string; category: string; monthlyLimit: number}
  ): void {
    if (action === 'edit') {
      this.startEdit(budget.id, budget.monthlyLimit);
    } else if (action === 'transactions') {
      this.viewTransactions(budget.category);
    } else if (action === 'remove') {
      this.store.remove(budget.id);
    }
  }

  public startEdit(id: string, monthlyLimit: number): void {
    this.editValue.set(String(monthlyLimit));
    this.store.setEditing(id);
  }

  public saveEdit(id: string): void {
    // Enter/Escape clear the editing state and the input's removal fires focusout; ignore that late save.
    if (this.store.editingId() !== id) {
      return;
    }
    const val = parseFloat(this.editValue());
    if (!isNaN(val) && val > 0) {
      this.store.update({id, monthlyLimit: val});
    }
    this.store.setEditing(null);
  }

  public cancelEdit(): void {
    this.store.setEditing(null);
  }

  public openAddBudget(): void {
    this.dialog
      .open<CreateBudgetRequest>(AddBudgetDialogComponent, {
        title: 'Add budget',
        size: 'sm',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(request => {
        if (request) {
          this.store.create(request);
        }
      });
  }

  public viewTransactions(category: string): void {
    const year = this.store.selectedYear();
    const month = this.store.selectedMonth();
    const from = DateRangeUtils.toIsoDate(new Date(Date.UTC(year, month - 1, 1)));
    const to = DateRangeUtils.toIsoDate(new Date(Date.UTC(year, month, 0)));
    void this.router.navigate([AppRoute.Transactions], {
      queryParams: {category, type: 'debit', from, to},
    });
  }

  public onMonthChange(month: Date): void {
    this.store.navigateToPeriod({year: month.getFullYear(), month: month.getMonth() + 1});
  }
}
