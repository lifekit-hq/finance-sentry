import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
  linkedSignal,
} from '@angular/core';
import {Router} from '@angular/router';
import {
  AlertComponent,
  ASYNC_STATE_MIN_DISPLAY_MS,
  ASYNC_STATE_SHOW_DELAY_MS,
  AsyncStateComponent,
  ButtonComponent,
  CmnDrawerService,
  EmptyStateComponent,
  FilterBarComponent,
  FilterBarSheetDirective,
  InstitutionAvatarComponent,
  ListItemRowComponent,
  ListSectionComponent,
  type Nullable,
  PageContainerComponent,
  PageScrollComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {TransactionFilterSheetComponent} from '../../components/transaction-filter-sheet/transaction-filter-sheet.component';
import {LEDGER_PERIODS} from '../../constants/ledger-period/ledger-period.constants';
import {type LedgerPeriod} from '../../models/ledger-period/ledger-period.model';
import {
  type GlobalTransactionDto,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {
  type AppliedTransactionFilter,
  EMPTY_TRANSACTION_FILTER,
  type TransactionFilterSelection,
} from '../../models/transaction/transaction-filter.model';
import {TransactionAmountPipe} from '../../pipes/transaction-amount.pipe';
import {TransactionAmountClassPipe} from '../../pipes/transaction-amount-class.pipe';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';
import {
  LEDGER_ALL_TIME_OUTFLOW_PHRASE,
  LEDGER_CUSTOM_RANGE_OUTFLOW_PHRASE,
  LEDGER_PERIOD_EMPTY_PHRASES,
  LEDGER_PERIOD_OUTFLOW_PHRASES,
} from './transaction-ledger.constants';

const SKELETON_SECTIONS = 2;
const SKELETON_ROWS_PER_SECTION = 4;
const DRAWER_WIDTH = '480px';

@Component({
  selector: 'fns-transaction-ledger',
  imports: [
    PageContainerComponent,
    PageScrollComponent,
    AlertComponent,
    AsyncStateComponent,
    ButtonComponent,
    EmptyStateComponent,
    FilterBarComponent,
    FilterBarSheetDirective,
    InstitutionAvatarComponent,
    InstitutionLogoPipe,
    ListItemRowComponent,
    ListSectionComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    SkeletonComponent,
    TransactionAmountClassPipe,
    TransactionAmountPipe,
    TransactionFilterSheetComponent,
  ],
  templateUrl: './transaction-ledger.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  host: {class: 'block md:h-full'},
  providers: [TransactionLedgerStore],
})
export class TransactionLedgerComponent {
  private readonly categoryStore = inject(CategoryStore);
  private readonly drawer = inject(CmnDrawerService);
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(TransactionLedgerStore);
  public readonly skeletonSections = Array.from({length: SKELETON_SECTIONS});
  public readonly skeletonRowsPerSection = SKELETON_ROWS_PER_SECTION;
  public readonly showDelay = ASYNC_STATE_SHOW_DELAY_MS;
  public readonly minDisplay = ASYNC_STATE_MIN_DISPLAY_MS;

  /**
   * The search box: typing writes it, and it follows the store whenever the store's search changes
   * (a debounced write, or back/forward to a different `q`).
   */
  public readonly query = linkedSignal(() => this.store.search());

  public readonly view = computed(() =>
    AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData: this.store.transactions().length > 0,
      errorMessage: this.store.errorMessage(),
      offline: this.connectivity.offline(),
      lastSyncedAt: this.store.lastSyncedAt(),
      // A revisit repaints the last visit's rows at once and revalidates them undimmed; a reload
      // on this visit (a filter change, a retry) keeps the rows dimmed until the new page lands.
      keepContent: !this.store.revalidating(),
    })
  );

  /** The quick period whose bounds equal the date filter; "All time" when there is none, any other range selects nothing. */
  public readonly activePeriod = computed(() =>
    LedgerPeriodUtils.match(this.store.from(), this.store.to())
  );

  /** Names the period the summary's outflow covers, so it never claims "this month" for another range. */
  public readonly outflowPeriodPhrase = computed(() => {
    const period = this.activePeriod();
    if (period) {
      return LEDGER_PERIOD_OUTFLOW_PHRASES[period];
    }
    return this.activeDateRangeLabel()
      ? LEDGER_CUSTOM_RANGE_OUTFLOW_PHRASE
      : LEDGER_ALL_TIME_OUTFLOW_PHRASE;
  });

  public readonly activeDateRangeLabel = computed(() => {
    const from = this.store.from();
    const to = this.store.to();
    if (!from && !to) {
      return null;
    }
    return from && to ? `${from} – ${to}` : from ? `from ${from}` : `until ${to}`;
  });

  /** Filters the sheet edits, as they stand in the store (synced from the URL). */
  public readonly activeSelection = computed<TransactionFilterSelection>(() => ({
    from: this.store.from(),
    to: this.store.to(),
    type: this.store.transactionType(),
    categories: this.store.categories(),
    accountId: this.store.accountId(),
  }));

  public readonly appliedFilters = computed<AppliedTransactionFilter[]>(() => {
    const filters: AppliedTransactionFilter[] = [];
    const range = this.activeDateRangeLabel();
    if (range) {
      const period = LEDGER_PERIODS.find(p => p.id === this.activePeriod());
      filters.push({
        id: 'date-range',
        label: period ? `Period: ${period.label}` : `Dates: ${range}`,
        remove: () => this.clearDateRange(),
      });
    }
    const type = this.store.transactionType();
    if (type) {
      filters.push({
        id: 'type',
        label: `Type: ${type === 'credit' ? 'In' : 'Out'}`,
        remove: () => this.selectType(null),
      });
    }
    for (const key of this.store.categories()) {
      const label = this.categoryStore.labelMap()[key] ?? MerchantCategoryUtils.format(key);
      filters.push({
        id: `category-${key}`,
        label: `Category: ${label}`,
        remove: () => this.removeCategory(key),
      });
    }
    const accountId = this.store.accountId();
    const account = accountId ? this.store.accounts().find(a => a.accountId === accountId) : null;
    if (account) {
      filters.push({
        id: 'account',
        label: `Account: ${account.label}`,
        remove: () => this.applySelection({...this.activeSelection(), accountId: null}),
      });
    }
    return filters;
  });

  /** Badge on the filter button: one per period, type, category and account in play. */
  public readonly activeFilterCount = computed(() => this.appliedFilters().length);

  /** Names the account and period in play so an empty list says what it is empty of. */
  public readonly emptyTitle = computed(() => {
    const accountId = this.store.accountId();
    const account = accountId && this.store.accounts().find(a => a.accountId === accountId)?.label;
    const period = this.activePeriod();
    const range = this.activeDateRangeLabel();
    const bounded = this.store.from() && this.store.to();
    const when = period
      ? `for ${LEDGER_PERIOD_EMPTY_PHRASES[period]}`
      : range
        ? `${bounded ? 'for ' : ''}${range}`
        : '';
    return ['No transactions', account ? `on ${account}` : '', when].filter(Boolean).join(' ');
  });

  /** Only a quick period narrower than 3M can be widened to it; a custom range or 3M itself needs clearing. */
  public readonly canWidenPeriod = computed(() => {
    const period = this.activePeriod();
    return period !== null && period !== '3m';
  });

  public readonly dayGroups = computed(() =>
    TransactionGroupUtils.groupByDay(this.store.transactions())
  );

  constructor() {
    this.store.applySearch(this.query);
  }

  public openDrawer(tx: GlobalTransactionDto): void {
    this.drawer.open(TransactionDrawerComponent, {
      title: tx.description,
      data: tx,
      width: DRAWER_WIDTH,
    });
  }

  /** An applied-filter chip was dismissed: drop that one filter. */
  public removeFilter(id: string): void {
    this.appliedFilters()
      .find(f => f.id === id)
      ?.remove();
  }

  public clearFilters(): void {
    this.applySelection(EMPTY_TRANSACTION_FILTER);
  }

  /** The empty state's way out: drops the search too, which the filter sheet does not own. */
  public clearAllFilters(): void {
    void this.router.navigate([], {
      queryParams: {account: null, q: null, from: null, to: null, type: null, category: null},
      queryParamsHandling: 'merge',
    });
  }

  public removeCategory(key: string): void {
    this.applySelection({
      ...this.activeSelection(),
      categories: this.store.categories().filter(c => c !== key),
    });
  }

  public selectPeriod(period: LedgerPeriod): void {
    void this.router.navigate([], {
      queryParams: LedgerPeriodUtils.dates(period),
      queryParamsHandling: 'merge',
    });
  }

  public clearDateRange(): void {
    this.applySelection({...this.activeSelection(), from: null, to: null});
  }

  public selectType(type: Nullable<TransactionType>): void {
    this.applySelection({...this.activeSelection(), type});
  }

  /** Writes the sheet filters to the URL; the search param is left as it is. */
  public applySelection(selection: TransactionFilterSelection): void {
    void this.router.navigate([], {
      queryParams: {
        from: selection.from,
        to: selection.to,
        type: selection.type,
        category: selection.categories.length > 0 ? selection.categories : null,
        account: selection.accountId,
      },
      queryParamsHandling: 'merge',
    });
  }
}
