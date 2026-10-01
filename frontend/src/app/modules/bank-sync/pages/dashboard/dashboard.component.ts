import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {
  AlertComponent,
  AreaChartComponent,
  BarChartComponent,
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnCellDirective,
  CmnColumnComponent,
  DataTableComponent,
  DonutChartComponent,
  IconComponent,
  PageHeaderComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {AppDecimalPipe} from '../../../../core/pipes/app-decimal.pipe';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {HISTORY_RANGE_LABELS} from '../../constants/dashboard/dashboard.constants';
import {type CategoryStat, type HistoryRange} from '../../models/dashboard/dashboard.model';
import {DashboardStore} from '../../store/dashboard/dashboard.store';

const HISTORY_RANGES: {label: string; value: HistoryRange}[] = [
  {label: '3M', value: '3m'},
  {label: '6M', value: '6m'},
  {label: '1Y', value: '1y'},
  {label: 'All', value: 'all'},
];

@Component({
  selector: 'fns-dashboard',
  imports: [
    AlertComponent,
    AppDecimalPipe,
    AreaChartComponent,
    BarChartComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    CmnCellDirective,
    CmnColumnComponent,
    DataTableComponent,
    DonutChartComponent,
    IconComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    PageHeaderComponent,
    RouterLink,
    SkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DashboardStore],
  template: `
    <div class="p-cmn-4 sm:p-cmn-6">
      <div class="mx-auto max-w-screen-lg space-y-cmn-6">
        <cmn-page-header
          class="block"
          title="Dashboard"
          subtitle="How your money is trending over time"
        />

        @if (store.errorMessage()) {
          <cmn-alert variant="error">{{ store.errorMessage() }}</cmn-alert>
        }

        @if (showEmptyState()) {
          <cmn-card>
            <div class="flex flex-col items-center gap-cmn-4 py-cmn-10 text-center">
              <div
                class="flex h-16 w-16 items-center justify-center rounded-cmn-full bg-accent-subtle text-accent-default"
              >
                <cmn-icon name="Link" size="lg" />
              </div>
              <div class="space-y-cmn-2">
                <h2 class="font-headline text-cmn-xl font-semibold text-text-primary">
                  Connect your first account
                </h2>
                <p class="max-w-md text-cmn-sm text-text-secondary">
                  Finance Sentry pulls balances, transactions, and holdings from your bank,
                  brokerage, and crypto providers. Connect an account to populate this dashboard.
                </p>
              </div>
              <cmn-button (clicked)="goToAccounts()" icon="Plus">Connect Account</cmn-button>
            </div>
          </cmn-card>
        } @else {
          <!--
            One hero for the headline figure and how it moved over the selected range. The range
            chips drive the chart below, the delta here and the category widgets, so they sit with
            the figure they change.
          -->
          <cmn-card>
            <div class="space-y-cmn-2">
              <div class="flex flex-wrap items-center justify-between gap-cmn-2">
                <span
                  class="font-label text-cmn-xs font-semibold uppercase tracking-wide text-text-secondary"
                >
                  Net worth
                </span>
                <div class="flex gap-cmn-1" role="group" aria-label="History range">
                  @for (r of ranges; track r.value) {
                    <cmn-chip
                      [selected]="store.historyRange() === r.value"
                      (clicked)="store.setHistoryRange(r.value)"
                      >{{ r.label }}</cmn-chip
                    >
                  }
                </div>
              </div>
              @if (store.isLoading()) {
                <cmn-skeleton height="2.25rem" width="50%" />
              } @else {
                <p
                  class="font-mono text-cmn-3xl font-semibold tabular-nums text-text-primary"
                  data-testid="net-worth-value"
                >
                  {{ store.totalBalanceFormatted() }}
                </p>
                @if (store.netWorthChangeFormatted()) {
                  <p
                    [class]="changeClass()"
                    class="font-label text-cmn-sm font-medium"
                    data-testid="net-worth-change"
                  >
                    {{ store.netWorthChangeFormatted() }}
                    @if (store.netWorthChangePercentFormatted()) {
                      ({{ store.netWorthChangePercentFormatted() }})
                    }
                    <span class="font-normal text-text-secondary">· {{ rangeLabel() }}</span>
                  </p>
                }
              }
              <!--
                Projected from contributions, never from the net-worth line: most of the book is
                market-marked, so a trend fitted to that line would forecast the market and call
                it a savings forecast. Hidden below three complete months by hasProjection().
              -->
              @if (store.hasProjection()) {
                <p [title]="store.projectionBasisLabel()" class="text-cmn-xs text-text-secondary">
                  At this pace:
                  <span class="font-medium text-text-primary">{{
                    store.projectedNetWorthFormatted()
                  }}</span>
                  in 12 months
                </p>
              }
            </div>
          </cmn-card>

          <div>
            @if (store.historyErrorMessage()) {
              <cmn-alert variant="error">{{ store.historyErrorMessage() }}</cmn-alert>
            } @else if (!store.historyHasHistory() && !store.isHistoryLoading()) {
              <cmn-alert variant="info"
                >Net worth history starts after tonight's snapshot.</cmn-alert
              >
            } @else {
              <cmn-area-chart
                [series]="store.netWorthAreaSeries()"
                [stacked]="true"
                label="Net worth by sleeve"
                currency="USD"
              />
              @if (store.netWorthStaleNotice()) {
                <div class="mt-cmn-2">
                  <cmn-alert variant="warning">{{ store.netWorthStaleNotice() }}</cmn-alert>
                </div>
              }
            }
          </div>

          <!--
            The in-progress month lives here and nowhere else. The charts below plot closed
            months only, so a partial figure never sits next to a complete one pretending to
            be comparable; here it is labelled month-to-date and paced against the trailing
            complete months, which is the comparison that actually means something mid-month.
          -->
          <div>
            <div class="mb-cmn-3 flex items-baseline justify-between">
              <span class="text-cmn-sm font-medium text-text-secondary">This month</span>
              <a
                [routerLink]="breakdownRoute"
                class="text-cmn-xs font-medium text-accent-default hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default"
                aria-label="View the breakdown of this month's money"
                >Breakdown →</a
              >
            </div>
            <cmn-card>
              <div class="grid grid-cols-3 divide-x divide-border-default">
                @for (tile of monthTiles; track tile.label) {
                  <button
                    [attr.aria-label]="tile.ariaLabel"
                    (click)="tile.open()"
                    type="button"
                    class="min-w-0 cursor-pointer space-y-cmn-1 px-cmn-2 text-left transition-opacity first:pl-0 last:pr-0 hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default sm:px-cmn-4"
                  >
                    <span
                      class="block truncate font-label text-cmn-xs font-semibold uppercase tracking-wide text-text-secondary"
                      >{{ tile.label }}</span
                    >
                    @if (store.isLoading()) {
                      <cmn-skeleton height="1.5rem" width="70%" />
                    } @else {
                      <span
                        class="block truncate font-mono text-cmn-lg font-semibold tabular-nums text-text-primary sm:text-cmn-2xl"
                        >{{ tile.value() }}</span
                      >
                      @if (tile.deltaLabel()) {
                        <span
                          [class]="paceClass(tile.delta())"
                          class="block font-label text-cmn-xs font-medium"
                          >{{ tile.deltaLabel() }}</span
                        >
                      }
                    }
                  </button>
                }
              </div>
            </cmn-card>
          </div>

          @if (store.hasCashFlow()) {
            <div>
              <cmn-bar-chart
                [series]="store.incomeVsSpendingBars()"
                label="Income vs Spending (complete months)"
                currency="USD"
              />
            </div>
          }

          <div class="grid grid-cols-1 gap-cmn-4 lg:grid-cols-3">
            <div>
              <cmn-donut-chart
                [segments]="store.categoryChartData()"
                [label]="topCategoriesLabel()"
                currency="USD"
              />
            </div>
            <div class="lg:col-span-2">
              <cmn-data-table
                [rows]="store.data()?.topCategories ?? []"
                (rowClick)="onCategoryClick($event)"
                class="grid gap-4"
                emptyMessage="No spending data available"
              >
                <cmn-column key="category" header="Category">
                  <ng-template let-row cmnCell>{{ row.category | merchantCategory }}</ng-template>
                </cmn-column>
                <cmn-column key="spend" header="Total Spend" align="right">
                  <ng-template let-row cmnCell>{{ row.totalSpend | money }}</ng-template>
                </cmn-column>
                <cmn-column key="pct" header="% of Total" align="right">
                  <ng-template let-row cmnCell
                    >{{ row.percentOfTotal | appDecimal: '1.1-1' }}%</ng-template
                  >
                </cmn-column>
              </cmn-data-table>
            </div>
          </div>
        }
      </div>
    </div>
  `,
})
export class DashboardComponent {
  private readonly router = inject(Router);

  public readonly store = inject(DashboardStore);
  public readonly ranges = HISTORY_RANGES;
  public readonly breakdownRoute = AppRoute.FlowBreakdown;
  public readonly showEmptyState = computed(
    () => !this.store.isLoading() && (this.store.data()?.accountCount ?? 0) === 0
  );
  public readonly rangeLabel = computed(() => HISTORY_RANGE_LABELS[this.store.historyRange()]);
  public readonly topCategoriesLabel = computed(
    () => `Top Spending Categories (${this.rangeLabel()})`
  );
  public readonly changeClass = computed(() =>
    this.paceClass(this.store.netWorthChangeDirection())
  );
  // The three month-to-date figures read as one card. Savings opens the audit view, which
  // shows every money bucket at once.
  public readonly monthTiles = [
    {
      label: 'Income',
      ariaLabel: 'View income details',
      value: this.store.monthlyInflowFormatted,
      delta: this.store.inflowPaceDelta,
      deltaLabel: this.store.inflowPaceLabel,
      open: (): void => this.goToIncome(),
    },
    {
      label: 'Spending',
      ariaLabel: 'View spending details',
      value: this.store.monthlySpendingFormatted,
      delta: this.store.spendingPaceDelta,
      deltaLabel: this.store.spendingPaceLabel,
      open: (): void => this.goToSpending(),
    },
    {
      label: 'Savings',
      ariaLabel: "View this month's savings breakdown",
      value: this.store.savingsRateMonthToDateFormatted,
      delta: this.store.savingsRatePaceDelta,
      deltaLabel: this.store.savingsRatePaceLabel,
      open: (): void => this.goToBreakdown(),
    },
  ] as const;

  // Green up, red down, muted when flat or unknown — the same convention the stat card uses.
  public paceClass(delta: number | null): string {
    if (!delta) {
      return 'text-text-secondary';
    }
    return delta > 0 ? 'text-status-success' : 'text-status-error';
  }

  public goToAccounts(): void {
    void this.router.navigateByUrl(AppRoute.AccountsList);
  }

  public goToIncome(): void {
    void this.router.navigate([AppRoute.Transactions], {queryParams: {type: 'credit'}});
  }

  public goToBreakdown(): void {
    void this.router.navigateByUrl(AppRoute.FlowBreakdown);
  }

  public goToSpending(): void {
    void this.router.navigate([AppRoute.Transactions], {queryParams: {type: 'debit'}});
  }

  public onCategoryClick(row: CategoryStat): void {
    void this.router.navigate([AppRoute.Transactions], {queryParams: {category: row.category}});
  }
}
