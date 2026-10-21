import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {type ChartPointClick} from '@lifekit-hq/charts-core';
import {type ChipRowChangeDetail, type ChipRowOption} from '@lifekit-hq/elements';
import {
  AlertComponent,
  AreaChartComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  DonutChartComponent,
  IconComponent,
  PageContainerComponent,
  SkeletonComponent,
  StatCardComponent,
} from '@lifekit-hq/ui';

import {AppDecimalPipe} from '../../../../core/pipes/app-decimal.pipe';
import {HeadlineBadgeComponent} from '../../../../shared/components/headline-badge/headline-badge.component';
import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {TopAlertComponent} from '../../../alerts/components/top-alert/top-alert.component';
import {NetWorthWhyMovedComponent} from '../../components/net-worth-why-moved/net-worth-why-moved.component';
import {
  HISTORY_RANGE_LABELS,
  HISTORY_RANGE_TILE_HEADINGS,
  NET_WORTH_SERIES_ROUTES,
} from '../../constants/dashboard/dashboard.constants';
import {type HistoryRange} from '../../models/dashboard/dashboard.model';
import {provideDashboardBridgeWindow} from '../../providers/net-worth-bridge-window.provider';
import {DashboardStore} from '../../store/dashboard/dashboard.store';
import {DashboardRangeUtils} from '../../utils/dashboard-range.utils';

// Cents on a headline amount are noise.
const WHOLE_DOLLARS = {maxFractionDigits: 0} as const;

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
    ButtonComponent,
    CardComponent,
    DonutChartComponent,
    HeadlineBadgeComponent,
    IconComponent,
    MoneyPipe,
    NetWorthWhyMovedComponent,
    RouterLink,
    SkeletonComponent,
    StatCardComponent,
    TopAlertComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [DashboardStore, provideDashboardBridgeWindow()],
  template: `
    <cmn-page-container spacing="none">
      <div>
        @if (view().offlineNotice; as notice) {
          <cmn-alert variant="info" class="mb-cmn-3 block" data-testid="offline-notice">{{
            notice
          }}</cmn-alert>
        }

        <cmn-async-state
          [status]="view().status"
          [errorMessage]="view().errorMessage"
          [isEmpty]="showEmptyState()"
          [errorPlacement]="errorPlacement()"
          class="flex flex-col gap-cmn-6"
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
            One hero: the headline figure and how it moved, the chart it comes from (its own legend
            names the cash and invested bands), then the range control under the chart
            (Robinhood/IBKR hero, #825 pick 2). The control stays page-level: it also drives "why it
            moved", "where it is" and the tiles below. Holding the chart scrubs the figure, the delta
            and the "where it is" rows to that point; release snaps them back.
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
              <!--
                The change line owns a fixed-height slot, so the hero keeps one height across every
                range, while the history loads and while scrubbing: with no number yet it leaves the
                slot empty instead of collapsing it.
              -->
              <div class="h-5 leading-5" data-testid="net-worth-change-slot">
                @if (store.isFirstLoad() || store.isHistoryFirstLoad()) {
                  <cmn-skeleton [className]="eitherPlaceholder()" height="1rem" width="40%" />
                } @else if (store.netWorthChangeFormatted()) {
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
                } @else if (store.netWorthChangeUnavailable()) {
                  <p
                    class="font-label text-cmn-sm font-medium leading-5 text-text-secondary"
                    data-testid="net-worth-change"
                  >
                    Change N/A
                  </p>
                }
              </div>
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
            Why the hero's change happened over the same range, in three lines: what you saved, what
            markets did, what exchange rates did. The full statement (M5) is on Breakdown.
          -->
          <fns-net-worth-why-moved [period]="tileHeading()" [detailsParams]="breakdownParams()" />

          <!--
            Where the money is now (or at the scrubbed point): the chart's bands as shares. The donut is
            interim until the shared allocation bar ships in @lifekit-hq/ui.
          -->
          @if (store.isHistoryFirstLoad()) {
            <!-- Another range's history is still on hand: its shares never sit under the new range. -->
            <cmn-card data-testid="net-worth-allocation-skeleton">
              <cmn-skeleton [className]="eitherPlaceholder()" height="10rem" />
            </cmn-card>
          } @else if (store.allocation().length > 0) {
            <cmn-card data-testid="net-worth-allocation">
              <h2 class="font-headline text-cmn-md font-semibold text-text-primary">Where it is</h2>
              <div
                class="mt-cmn-3 grid grid-cols-1 items-center gap-cmn-4 sm:grid-cols-[10rem_1fr]"
              >
                <cmn-donut-chart
                  [segments]="store.allocationSegments()"
                  [currency]="store.baseCurrency()"
                  [chrome]="false"
                  [showLegend]="false"
                  class="mx-auto block h-56 w-40"
                  label="Where your net worth is"
                />
                <ul class="divide-y divide-border-default">
                  @for (row of store.allocation(); track row.label) {
                    <li data-testid="net-worth-allocation-row">
                      <!-- A band no accounts page lists on its own (other sources, no split) opens nothing. -->
                      <a
                        [routerLink]="row.route"
                        class="flex items-center gap-cmn-2 py-cmn-2 text-cmn-sm hover:opacity-80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default"
                      >
                        <span
                          [style.background-color]="row.color"
                          class="inline-block h-2.5 w-2.5 shrink-0 rounded-cmn-full"
                          aria-hidden="true"
                        ></span>
                        <span class="min-w-0 flex-1 truncate text-text-primary">{{
                          row.label
                        }}</span>
                        <span class="tabular-nums text-text-secondary">{{
                          row.percent === null ? '—' : (row.percent | appDecimal: '1.0-0') + '%'
                        }}</span>
                        <span class="w-24 text-right font-medium tabular-nums text-text-primary">{{
                          row.value | money: store.baseCurrency() : wholeDollars
                        }}</span>
                      </a>
                    </li>
                  }
                </ul>
              </div>
            </cmn-card>
          }

          <!--
            Spending first, against the usual pace of the closed months, then income with the share of
            it kept. Totals for the selected range, in-progress month included, so the tiles tell the
            same story as the hero.
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
            <div class="grid grid-cols-2 gap-cmn-4">
              <a
                [routerLink]="transactionsRoute"
                [queryParams]="spendingParams()"
                class="block rounded-cmn-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default"
                data-testid="home-spent-link"
                aria-label="View spending details"
              >
                <cmn-stat-card
                  [value]="store.isFirstLoad() ? '—' : store.windowSpendingFormatted()"
                  [delta]="store.isFirstLoad() || store.windowSpendingVsUsual() === null ? null : 0"
                  [deltaLabel]="store.isFirstLoad() ? '' : (store.windowSpendingVsUsual() ?? '')"
                  [loading]="tilesLoading()"
                  label="Spent"
                />
              </a>
              <a
                [routerLink]="transactionsRoute"
                [queryParams]="incomeParams()"
                class="block rounded-cmn-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-default"
                data-testid="home-income-link"
                aria-label="View income details"
              >
                <cmn-stat-card
                  [value]="store.isFirstLoad() ? '—' : store.windowInflowFormatted()"
                  [delta]="store.isFirstLoad() ? null : store.windowSavingsRatePercent()"
                  [deltaLabel]="store.isFirstLoad() ? '' : savedLabel()"
                  [loading]="tilesLoading()"
                  label="Income"
                />
              </a>
            </div>
          </div>

          <!-- One insight, from Alerts: the newest unread alert, with the rest a tap away. -->
          <fns-top-alert />
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
  public readonly wholeDollars = WHOLE_DOLLARS;
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
  // The stat card's skeleton always sweeps, so a failed load settles the tiles on a dash instead:
  // another range's figures never sit under this window's label.
  public readonly tilesLoading = computed(
    () => this.store.isFirstLoad() && !this.store.isFirstLoadFailed()
  );
  public readonly rangeOptions = HISTORY_RANGES;
  public readonly breakdownRoute = AppRoute.FlowBreakdown;
  public readonly accountsRoute = AppRoute.AccountsList;
  public readonly transactionsRoute = AppRoute.Transactions;
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
  // The breakdown follows the selected window like the tiles do: the same days, and the same
  // history loaded around them so transfer pairs resolve the way the tiles resolved them.
  public readonly breakdownParams = computed(() =>
    DashboardRangeUtils.breakdownParams(this.store.historyRange())
  );
  // The selected range's date bounds, so a tile lists the same window it totals.
  public readonly spendingParams = computed(() => ({
    type: 'debit',
    ...DashboardRangeUtils.windowDates(this.store.historyRange()),
  }));
  public readonly incomeParams = computed(() => ({
    type: 'credit',
    ...DashboardRangeUtils.windowDates(this.store.historyRange()),
  }));
  public readonly rangeLabel = computed(() => HISTORY_RANGE_LABELS[this.store.historyRange()]);
  public readonly tileHeading = computed(
    () => HISTORY_RANGE_TILE_HEADINGS[this.store.historyRange()]
  );
  public readonly savedLabel = computed(() => {
    const rate = this.store.windowSavingsRatePercent();
    return rate === null ? '' : `Saved ${rate}%`;
  });
  public readonly changeClass = computed(() =>
    this.paceClass(this.store.netWorthChangeDirection())
  );

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

  // A band is a sleeve of the book, so it opens the accounts page that lists that sleeve.
  public onNetWorthClick(click: ChartPointClick): void {
    const route = NET_WORTH_SERIES_ROUTES.get(click.seriesLabel);
    if (route) {
      void this.router.navigateByUrl(route);
    }
  }
}
