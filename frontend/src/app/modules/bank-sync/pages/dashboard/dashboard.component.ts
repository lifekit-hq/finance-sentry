import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {
  AlertComponent,
  AreaChartComponent,
  AsyncStateComponent,
  BarChartComponent,
  ButtonComponent,
  CardComponent,
  CmnCellDirective,
  CmnColumnComponent,
  DataTableComponent,
  DonutChartComponent,
  IconComponent,
  PageContainerComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {AppDecimalPipe} from '../../../../core/pipes/app-decimal.pipe';
import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {FireTileComponent} from '../../components/fire-tile/fire-tile.component';
import {
  HISTORY_RANGE_LABELS,
  HISTORY_RANGE_TILE_HEADINGS,
} from '../../constants/dashboard/dashboard.constants';
import {type CategoryStat, type HistoryRange} from '../../models/dashboard/dashboard.model';
import {DashboardStore} from '../../store/dashboard/dashboard.store';
import {DashboardRangeUtils} from '../../utils/dashboard-range.utils';

// lk-segmented reads `value` as a string; the options are the preset keys.
const HISTORY_RANGES: {label: string; value: HistoryRange}[] = [
  {label: '1W', value: '1w'},
  {label: 'MTD', value: 'mtd'},
  {label: '1M', value: '1m'},
  {label: '3M', value: '3m'},
  {label: 'YTD', value: 'ytd'},
  {label: '1Y', value: '1y'},
  {label: 'ALL', value: 'all'},
];

@Component({
  selector: 'fns-dashboard',
  imports: [
    PageContainerComponent,
    AlertComponent,
    AppDecimalPipe,
    AreaChartComponent,
    AsyncStateComponent,
    BarChartComponent,
    ButtonComponent,
    CardComponent,
    CmnCellDirective,
    CmnColumnComponent,
    DataTableComponent,
    DonutChartComponent,
    FireTileComponent,
    IconComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    RouterLink,
    SkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [DashboardStore],
  template: `
    <cmn-page-container spacing="none">
      <div>
        <p class="text-cmn-sm text-text-secondary">How your money is trending over time</p>

        @if (view().offlineNotice; as notice) {
          <cmn-alert variant="info" class="mt-cmn-3 block" data-testid="offline-notice">{{
            notice
          }}</cmn-alert>
        }

        <cmn-async-state
          [status]="view().status"
          [errorMessage]="view().errorMessage"
          [isEmpty]="showEmptyState()"
          [errorPlacement]="errorPlacement()"
          class="block space-y-cmn-6"
        >
          <cmn-button (clicked)="retry()" error-action variant="secondary" size="sm"
            >Retry</cmn-button
          >
          <cmn-card empty>
            <div class="flex flex-col items-center gap-cmn-4 py-cmn-10 text-center">
              <div
                class="flex h-16 w-16 items-center justify-center rounded-cmn-full bg-accent-subtle text-accent-default"
              >
                <cmn-icon name="Link" size="lg" />
              </div>
              <div class="space-y-cmn-2">
                <h2 class="font-headline text-cmn-lg font-semibold text-text-primary">
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

          <!--
            One hero: the headline figure and how it moved, the chart it comes from, then the range
            control under the chart (Robinhood/IBKR hero, #825 pick 2). The control stays page-level:
            it also drives the tiles and category widgets below. Holding the chart scrubs the figure
            and the delta to that point; release snaps both back.
          -->
          <cmn-card>
            <div class="space-y-cmn-2">
              <div class="flex items-center justify-between gap-cmn-2">
                <a
                  [routerLink]="accountsRoute"
                  class="font-label text-cmn-xs font-semibold uppercase tracking-wide text-text-secondary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default"
                  data-testid="net-worth-link"
                  aria-label="View accounts behind net worth"
                >
                  Net worth →
                </a>
              </div>
              @if (store.isLoading()) {
                <cmn-skeleton height="1.875rem" width="50%" />
              } @else {
                <p
                  class="text-cmn-2xl font-semibold tabular-nums text-text-primary"
                  data-testid="net-worth-value"
                >
                  {{ store.totalBalanceFormatted() }}
                </p>
              }
              <!--
                Both lines under the figure own a fixed-height slot, so the hero keeps one height
                across every range, while the history loads and while scrubbing: a line that does
                not apply (or has no number yet) leaves its slot empty instead of collapsing it.
              -->
              <div class="h-5 leading-5" data-testid="net-worth-change-slot">
                @if (store.isLoading() || store.isHistoryLoading()) {
                  <cmn-skeleton height="1rem" width="40%" />
                } @else if (store.netWorthChangeFormatted()) {
                  <p
                    [class]="changeClass()"
                    class="font-label text-cmn-sm font-medium leading-5"
                    data-testid="net-worth-change"
                  >
                    {{ store.netWorthChangeFormatted() }}
                    @if (store.netWorthChangePercentFormatted()) {
                      ({{ store.netWorthChangePercentFormatted() }})
                    }
                    <span class="font-normal text-text-secondary"
                      >· {{ store.scrubDateFormatted() ?? rangeLabel() }}</span
                    >
                  </p>
                }
              </div>
              <!--
                Projected from contributions, never from the net-worth line: most of the book is
                market-marked, so a trend fitted to that line would forecast the market and call
                it a savings forecast. Empty below three complete months (hasProjection()).
              -->
              <div class="h-4 leading-4" data-testid="net-worth-projection-slot">
                @if (store.isLoading() || store.isHistoryLoading()) {
                  <cmn-skeleton height="0.875rem" width="55%" />
                } @else if (store.hasProjection()) {
                  <p
                    [title]="store.projectionBasisLabel()"
                    class="text-cmn-xs leading-4 text-text-secondary"
                  >
                    At this pace:
                    <span class="font-medium text-text-primary">{{
                      store.projectedNetWorthFormatted()
                    }}</span>
                    in 12 months
                  </p>
                }
              </div>
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
                  [currency]="store.baseCurrency()"
                  [scrubbable]="true"
                  (scrub)="store.setScrubIndex($event.index)"
                  (scrubEnd)="store.setScrubIndex(null)"
                  label="Net worth by sleeve"
                />
                @if (store.netWorthStaleNotice()) {
                  <div class="mt-cmn-2">
                    <cmn-alert variant="warning">{{ store.netWorthStaleNotice() }}</cmn-alert>
                  </div>
                }
              }
              <lk-segmented
                [options]="rangeOptions"
                [value]="store.historyRange()"
                (lk-segmented-change)="onRangeChange($event)"
                label="History range"
              />
            </div>
          </cmn-card>

          <!--
            Computed on read from the same honest monthly flow as the savings rate, with both
            assumptions stated in the tile. Renders nothing below three complete months.
          -->
          <fns-fire-tile />

          <!--
            Totals for the selected range, in-progress month included, so the tiles tell the same
            story as the hero and the charts. The charts below plot closed months only, so a
            partial figure never sits next to a complete one pretending to be comparable.
          -->
          <div>
            <div class="mb-cmn-3 flex items-baseline justify-between">
              <span class="text-cmn-sm font-medium text-text-secondary">{{ tileHeading() }}</span>
              <a
                [routerLink]="breakdownRoute"
                [queryParams]="breakdownParams()"
                class="text-cmn-xs font-medium text-accent-default hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default"
                aria-label="View the breakdown of the selected window's money"
                >Breakdown →</a
              >
            </div>
            <cmn-card>
              <div class="grid grid-cols-2 gap-y-cmn-3 sm:grid-cols-3">
                @for (tile of monthTiles; track tile.label) {
                  <button
                    [attr.aria-label]="tile.ariaLabel"
                    (click)="tile.open()"
                    type="button"
                    class="min-w-0 cursor-pointer space-y-cmn-1 border-border-default px-cmn-4 text-left transition-opacity first:pl-0 hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default max-sm:[&:nth-child(2)]:border-l max-sm:last:col-span-2 max-sm:last:border-t max-sm:last:px-0 max-sm:last:pt-cmn-3 sm:border-l sm:first:border-l-0 sm:last:pr-0"
                  >
                    <span
                      class="block truncate font-label text-cmn-xs font-semibold uppercase tracking-wide text-text-secondary"
                      >{{ tile.label }}</span
                    >
                    @if (store.isLoading()) {
                      <span class="block h-6 sm:h-[1.5625rem]">
                        <cmn-skeleton height="100%" width="70%" />
                      </span>
                    } @else {
                      <span
                        class="block truncate text-cmn-md font-semibold tabular-nums text-text-primary sm:text-cmn-xl"
                        >{{ tile.value() }}</span
                      >
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
                [currency]="store.baseCurrency()"
                label="Income vs Spending (complete months)"
              />
            </div>
          }

          <div class="grid grid-cols-1 gap-cmn-4 lg:grid-cols-3">
            <div>
              <cmn-donut-chart
                [segments]="store.categoryChartData()"
                [label]="topCategoriesLabel()"
                [currency]="store.baseCurrency()"
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
                  <ng-template let-row cmnCell>{{
                    row.totalSpend | money: store.baseCurrency()
                  }}</ng-template>
                </cmn-column>
                <cmn-column key="pct" header="% of Total" align="right">
                  <ng-template let-row cmnCell
                    >{{ row.percentOfTotal | appDecimal: '1.1-1' }}%</ng-template
                  >
                </cmn-column>
              </cmn-data-table>
            </div>
          </div>
        </cmn-async-state>
      </div>
    </cmn-page-container>
  `,
})
export class DashboardComponent {
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(DashboardStore);
  public readonly rangeOptions = HISTORY_RANGES;
  public readonly breakdownRoute = AppRoute.FlowBreakdown;
  public readonly accountsRoute = AppRoute.AccountsList;
  // An error leaves `data` null, which must never read as "no accounts connected".
  public readonly showEmptyState = computed(() => {
    const data = this.store.data();
    return (
      !this.store.isLoading() && !this.store.hasError() && data !== null && data.accountCount === 0
    );
  });
  // With no data to keep, the error replaces the zeroed widgets instead of sitting above them.
  public readonly errorPlacement = computed(() =>
    this.store.data() === null ? 'replace' : 'above'
  );
  // Loading stays out of the status: the dashboard renders per-widget skeletons in place.
  public readonly view = computed(() =>
    AsyncViewUtils.resolve({
      isLoading: false,
      hasData: this.store.data() !== null,
      errorMessage: this.store.errorMessage(),
      offline: this.connectivity.offline(),
      lastSyncedAt: this.store.lastSyncedAt(),
    })
  );
  public readonly breakdownParams = computed(() =>
    DashboardRangeUtils.breakdownParams(this.store.historyRange())
  );
  public readonly rangeLabel = computed(() => HISTORY_RANGE_LABELS[this.store.historyRange()]);
  public readonly tileHeading = computed(
    () => HISTORY_RANGE_TILE_HEADINGS[this.store.historyRange()]
  );
  public readonly topCategoriesLabel = computed(
    () => `Top Spending Categories (${this.rangeLabel()})`
  );
  public readonly changeClass = computed(() =>
    this.paceClass(this.store.netWorthChangeDirection())
  );
  // The three window figures read as one card. Savings opens the audit view, which
  // shows every money bucket at once.
  public readonly monthTiles = [
    {
      label: 'Income',
      ariaLabel: 'View income details',
      value: this.store.windowInflowFormatted,
      open: (): void => this.goToIncome(),
    },
    {
      label: 'Spending',
      ariaLabel: 'View spending details',
      value: this.store.windowSpendingFormatted,
      open: (): void => this.goToSpending(),
    },
    {
      label: 'Savings',
      ariaLabel: 'View the savings breakdown',
      value: this.store.windowSavingsRateFormatted,
      open: (): void => this.goToBreakdown(),
    },
  ] as const;

  // Green up, red down, muted when flat or unknown — the same convention the stat card uses.
  public paceClass(delta: number | null): string {
    if (!delta) {
      return 'text-text-secondary';
    }
    return delta > 0 ? GAIN_TEXT_CLASS : LOSS_TEXT_CLASS;
  }

  public onRangeChange(event: Event): void {
    const {value} = (event as CustomEvent<{value: HistoryRange}>).detail;
    this.store.setHistoryRange(value);
  }

  public retry(): void {
    this.store.load(this.store.historyRange());
    this.store.loadNetWorthHistory(this.store.historyRange());
  }

  public goToAccounts(): void {
    void this.router.navigateByUrl(AppRoute.AccountsList);
  }

  public goToIncome(): void {
    void this.router.navigate([AppRoute.Transactions], {
      queryParams: {type: 'credit', ...this.rangeDates()},
    });
  }

  // The breakdown follows the selected window like the tiles do: the same days, and the same
  // history loaded around them so transfer pairs resolve the way the tiles resolved them.
  public goToBreakdown(): void {
    void this.router.navigate([AppRoute.FlowBreakdown], {
      queryParams: this.breakdownParams(),
    });
  }

  public goToSpending(): void {
    void this.router.navigate([AppRoute.Transactions], {
      queryParams: {type: 'debit', ...this.rangeDates()},
    });
  }

  // Top spendings counts outflows only, so the drill-down lists the category's debits.
  public onCategoryClick(row: CategoryStat): void {
    void this.router.navigate([AppRoute.Transactions], {
      queryParams: {type: 'debit', category: row.category, ...this.rangeDates()},
    });
  }

  // The selected range's date bounds, so a drill-down lists the same window the tile totals.
  private rangeDates(): {from?: string; to?: string} {
    return DashboardRangeUtils.windowDates(this.store.historyRange());
  }
}
