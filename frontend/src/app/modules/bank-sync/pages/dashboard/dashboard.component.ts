import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {type ChartPointClick, type DonutSegmentClick} from '@lifekit-hq/charts-core';
import {type ChipRowChangeDetail, type ChipRowOption} from '@lifekit-hq/elements';
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
import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {InvestmentsCardComponent} from '../../../holdings/components/investments-card/investments-card.component';
import {FireTileComponent} from '../../components/fire-tile/fire-tile.component';
import {NetWorthBridgeComponent} from '../../components/net-worth-bridge/net-worth-bridge.component';
import {NetWorthProjectionComponent} from '../../components/net-worth-projection/net-worth-projection.component';
import {
  CASH_FLOW_SERIES_TYPES,
  HISTORY_RANGE_LABELS,
  HISTORY_RANGE_TILE_HEADINGS,
  NET_WORTH_SERIES_ROUTES,
} from '../../constants/dashboard/dashboard.constants';
import {type CategoryStat, type HistoryRange} from '../../models/dashboard/dashboard.model';
import {DashboardStore} from '../../store/dashboard/dashboard.store';
import {DashboardRangeUtils} from '../../utils/dashboard-range.utils';

// Hides the skeleton's moving sheen (its ::after), leaving the still block.
const STATIC_PLACEHOLDER_CLASS = 'after:hidden';

// Seven presets are more than lk-segmented holds, so they sit in a chip row keyed by the preset.
const HISTORY_RANGES: ChipRowOption[] = [
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
    HeadlineBadgeComponent,
    IconComponent,
    InvestmentsCardComponent,
    NetWorthProjectionComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    NetWorthBridgeComponent,
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
              @if (store.isFirstLoad()) {
                <cmn-skeleton
                  [className]="dataPlaceholder()"
                  class="block"
                  height="1.875rem"
                  width="50%"
                />
              } @else {
                <div class="flex items-center gap-cmn-2">
                  <p
                    class="text-cmn-2xl font-semibold tabular-nums text-text-primary"
                    data-testid="net-worth-value"
                  >
                    {{ store.totalBalanceFormatted() }}
                  </p>
                  <fns-headline-badge
                    [headline]="store.netWorthHeadline()"
                    label="Net worth"
                    data-testid="net-worth-quality"
                  />
                </div>
              }
              <!-- Live cash / invested split of the book; empty while scrubbing or when unknown. -->
              <div class="h-4 leading-4" data-testid="net-worth-split-slot">
                @if (store.isFirstLoad()) {
                  <cmn-skeleton [className]="dataPlaceholder()" height="0.875rem" width="45%" />
                } @else if (store.bookSplitFormatted() || store.bookSplitUnavailable()) {
                  <div class="flex items-center gap-cmn-2">
                    <p
                      class="text-cmn-xs leading-4 text-text-secondary tabular-nums"
                      data-testid="net-worth-split"
                    >
                      {{ store.bookSplitFormatted() || 'Cash / invested split N/A' }}
                    </p>
                    <fns-headline-badge
                      [headline]="store.bookSplitHeadline()"
                      label="Cash and invested split"
                      data-testid="net-worth-split-quality"
                    />
                  </div>
                }
              </div>
              <!--
                Both lines under the figure own a fixed-height slot, so the hero keeps one height
                across every range, while the history loads and while scrubbing: a line that does
                not apply (or has no number yet) leaves its slot empty instead of collapsing it.
              -->
              <div class="h-5 leading-5" data-testid="net-worth-change-slot">
                @if (store.isFirstLoad() || store.isHistoryFirstLoad()) {
                  <cmn-skeleton [className]="eitherPlaceholder()" height="1rem" width="40%" />
                } @else if (store.netWorthChangeFormatted()) {
                  <div class="flex items-center gap-cmn-2">
                    <p
                      [class]="changeClass()"
                      class="block min-w-0 truncate font-label text-cmn-sm font-medium leading-5"
                      data-testid="net-worth-change"
                    >
                      <span class="font-normal text-text-secondary">Change</span>
                      {{ store.netWorthChangeFormatted() }}
                      @if (store.netWorthChangePercentFormatted()) {
                        ({{ store.netWorthChangePercentFormatted() }})
                      } @else if (store.netWorthScopeNote()) {
                        <span
                          [attr.title]="store.netWorthScopeNote()"
                          [attr.aria-label]="store.netWorthScopeNote()"
                          class="font-normal text-text-secondary"
                          data-testid="net-worth-scope-note"
                          >· {{ store.netWorthScopeNote() }}</span
                        >
                      }
                      <span class="font-normal text-text-secondary"
                        >· {{ store.scrubDateFormatted() ?? rangeLabel() }}</span
                      >
                    </p>
                    <fns-headline-badge
                      [headline]="store.netWorthChangeHeadline()"
                      label="Net worth change"
                      data-testid="net-worth-change-quality"
                    />
                  </div>
                } @else if (store.netWorthChangeUnavailable()) {
                  <div class="flex items-center gap-cmn-2">
                    <p
                      class="font-label text-cmn-sm font-medium leading-5 text-text-secondary"
                      data-testid="net-worth-change"
                    >
                      Change N/A
                    </p>
                    <fns-headline-badge
                      [headline]="store.netWorthChangeHeadline()"
                      label="Net worth change"
                      data-testid="net-worth-change-quality"
                    />
                  </div>
                }
              </div>
              <fns-net-worth-projection />
              @if (store.historyErrorMessage()) {
                <div class="flex items-center gap-cmn-2" data-testid="net-worth-history-error">
                  <cmn-alert variant="error" class="min-w-0 flex-1">{{
                    store.historyErrorMessage()
                  }}</cmn-alert>
                  <cmn-button (clicked)="retryHistory()" variant="secondary" size="sm"
                    >Retry</cmn-button
                  >
                </div>
                @if (store.isHistoryFirstLoadFailed()) {
                  <cmn-skeleton
                    [className]="staticPlaceholder"
                    height="19.8125rem"
                    data-testid="net-worth-chart-skeleton"
                  />
                }
              } @else if (
                store.isHistoryFirstLoad() || (!store.historyHasHistory() && store.isFirstLoad())
              ) {
                <cmn-skeleton
                  [className]="eitherPlaceholder()"
                  height="19.8125rem"
                  data-testid="net-worth-chart-skeleton"
                />
              } @else if (!store.historyHasHistory()) {
                <cmn-alert variant="info"
                  >Net worth history starts after tonight's snapshot.</cmn-alert
                >
              } @else {
                <cmn-area-chart
                  [series]="store.netWorthAreaSeries()"
                  [stacked]="true"
                  [currency]="store.baseCurrency()"
                  [scrubbable]="true"
                  [label]="store.netWorthChartLabel()"
                  (scrub)="store.setScrubIndex($event.index)"
                  (pointClick)="onNetWorthClick($event)"
                  (scrubEnd)="store.setScrubIndex(null)"
                />
              }
              <lk-chip-row
                [options]="rangeOptions"
                [selected]="[store.historyRange()]"
                (lk-chip-row-change)="onRangeChange($event)"
                label="History range"
              />
            </div>
          </cmn-card>

          <!--
            Why the hero's figure moved over the same range (M5): savings, investment return, currency
            moves and accounts linked or removed, with whatever they leave unexplained on its own row.
          -->
          <fns-net-worth-bridge />

          <fns-investments-card />

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
                    class="min-w-0 cursor-pointer space-y-cmn-1 border-border-default px-cmn-4 text-left transition-opacity duration-cmn-short ease-cmn-standard first:pl-0 hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default max-sm:[&:nth-child(2)]:border-l max-sm:last:col-span-2 max-sm:last:border-t max-sm:last:px-0 max-sm:last:pt-cmn-3 sm:border-l sm:first:border-l-0 sm:last:pr-0"
                  >
                    <span
                      class="block truncate font-label text-cmn-xs font-semibold uppercase tracking-wide text-text-secondary"
                      >{{ tile.label }}</span
                    >
                    @if (store.isFirstLoad()) {
                      <span class="block h-6 sm:h-[1.5625rem]">
                        <cmn-skeleton [className]="dataPlaceholder()" height="100%" width="70%" />
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

          @if (store.isFirstLoad()) {
            <cmn-skeleton
              [className]="dataPlaceholder()"
              height="19.8125rem"
              data-testid="cash-flow-chart-skeleton"
            />
          } @else if (store.hasCashFlow()) {
            <div>
              <cmn-bar-chart
                [series]="store.incomeVsSpendingBars()"
                [currency]="store.baseCurrency()"
                (barClick)="onCashFlowBarClick($event)"
                label="Income vs Spending (complete months)"
              />
            </div>
          }

          <div class="grid grid-cols-1 gap-cmn-4 lg:grid-cols-3">
            @if (store.isFirstLoad()) {
              <cmn-skeleton
                [className]="dataPlaceholder()"
                height="12rem"
                data-testid="category-donut-skeleton"
              />
              <cmn-skeleton
                [className]="dataPlaceholder()"
                class="lg:col-span-2"
                height="12rem"
                data-testid="category-table-skeleton"
              />
            } @else {
              <div>
                <cmn-donut-chart
                  [segments]="store.categoryChartData()"
                  [label]="topCategoriesLabel()"
                  [currency]="store.baseCurrency()"
                  (segmentClick)="onCategorySegmentClick($event)"
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
            }
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
  public readonly staticPlaceholder = STATIC_PLACEHOLDER_CLASS;
  // A failed range load leaves the sized placeholder in place but still: a sweep would promise
  // figures that are not coming.
  public readonly dataPlaceholder = computed(() =>
    this.store.isFirstLoadFailed() ? STATIC_PLACEHOLDER_CLASS : ''
  );
  public readonly eitherPlaceholder = computed(() =>
    this.store.isFirstLoadFailed() || this.store.isHistoryFirstLoadFailed()
      ? STATIC_PLACEHOLDER_CLASS
      : ''
  );
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
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.setHistoryRange(value as HistoryRange);
  }

  public retryHistory(): void {
    this.store.loadNetWorthHistory(this.store.historyRange());
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
    this.openCategory(row.category);
  }

  // A band is a sleeve of the book, so it opens the accounts page that lists that sleeve.
  public onNetWorthClick(click: ChartPointClick): void {
    const route = NET_WORTH_SERIES_ROUTES.get(click.seriesLabel);
    if (route) {
      void this.router.navigateByUrl(route);
    }
  }

  // A bar is one month of income or spending, so it lists that month's credits or debits.
  public onCashFlowBarClick(click: ChartPointClick): void {
    const type = CASH_FLOW_SERIES_TYPES.get(click.seriesLabel);
    const monthKey = this.store.incomeVsSpendingMonthKeys()[click.index];
    if (!type || !monthKey) {
      return;
    }
    void this.router.navigate([AppRoute.Transactions], {
      queryParams: {type, ...DashboardRangeUtils.monthDates(monthKey)},
    });
  }

  // The folded "Other categories" slice stands for several categories, so it opens nothing.
  public onCategorySegmentClick(click: DonutSegmentClick): void {
    const category = this.store.categoryChartKeys()[click.index];
    if (category) {
      this.openCategory(category);
    }
  }

  private openCategory(category: string): void {
    void this.router.navigate([AppRoute.Transactions], {
      queryParams: {type: 'debit', category, ...this.rangeDates()},
    });
  }

  // The selected range's date bounds, so a drill-down lists the same window the tile totals.
  private rangeDates(): {from?: string; to?: string} {
    return DashboardRangeUtils.windowDates(this.store.historyRange());
  }
}
