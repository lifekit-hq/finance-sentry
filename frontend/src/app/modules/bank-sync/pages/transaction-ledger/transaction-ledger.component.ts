import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {takeUntilDestroyed, toObservable} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {Router} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnDrawerService,
  EmptyStateComponent,
  InputComponent,
  InstitutionAvatarComponent,
  ListItemRowComponent,
  PageContainerComponent,
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

const SKELETON_ROWS = 8;
const DRAWER_WIDTH = '480px';
const FILTER_SHEET_WIDTH = '420px';

@Component({
  selector: 'fns-transaction-ledger',
  imports: [
    PageContainerComponent,
    AlertComponent,
    AsyncStateComponent,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    EmptyStateComponent,
    InputComponent,
    InstitutionAvatarComponent,
    InstitutionLogoPipe,
    ListItemRowComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    ReactiveFormsModule,
    SkeletonComponent,
    TransactionAmountClassPipe,
    TransactionAmountPipe,
  ],
  templateUrl: './transaction-ledger.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  host: {class: 'block min-h-full md:h-full'},
  providers: [TransactionLedgerStore],
})
export class TransactionLedgerComponent {
  private readonly categoryStore = inject(CategoryStore);
  private readonly drawer = inject(CmnDrawerService);
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(TransactionLedgerStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});

  public readonly searchControl = new FormControl(this.store.search(), {nonNullable: true});

  public readonly periods = LEDGER_PERIODS;

  public readonly view = computed(() =>
    AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData: this.store.transactions().length > 0,
      errorMessage: this.store.errorMessage(),
      offline: this.connectivity.offline(),
      lastSyncedAt: this.store.lastSyncedAt(),
    })
  );

  /** The quick period whose bounds equal the date filter; any other range selects none. */
  public readonly activePeriod = computed(() =>
    LedgerPeriodUtils.match(this.store.from(), this.store.to())
  );

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
    return filters;
  });

  /** Badge on the filter button: one per period, type and category in play. */
  public readonly activeFilterCount = computed(() => this.appliedFilters().length);

  public readonly dayGroups = computed(() =>
    TransactionGroupUtils.groupByDay(this.store.transactions())
  );

  constructor() {
    this.store.applySearch(this.searchControl.valueChanges);
    this.followSearch();
  }

  /** State → box: back/forward (or a new deep link) rewrites the box when it differs. */
  private followSearch(): void {
    toObservable(this.store.search)
      .pipe(takeUntilDestroyed())
      .subscribe(search => {
        if (search !== this.searchControl.value) {
          this.searchControl.setValue(search);
        }
      });
  }

  public openDrawer(tx: GlobalTransactionDto): void {
    this.drawer.open(TransactionDrawerComponent, {
      title: tx.description,
      data: tx,
      width: DRAWER_WIDTH,
    });
  }

  public selectAccount(accountId: Nullable<string>): void {
    void this.router.navigate([], {
      queryParams: {account: accountId},
      queryParamsHandling: 'merge',
    });
  }

  public openFilters(): void {
    this.drawer
      .open<
        TransactionFilterSelection,
        TransactionFilterSelection,
        TransactionFilterSheetComponent
      >(TransactionFilterSheetComponent, {
        title: 'Filters',
        data: this.activeSelection(),
        mode: 'responsive',
        width: FILTER_SHEET_WIDTH,
      })
      .afterClosed()
      .subscribe(selection => {
        if (selection) {
          this.applySelection(selection);
        }
      });
  }

  public clearFilters(): void {
    this.applySelection(EMPTY_TRANSACTION_FILTER);
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

  /** Writes the sheet filters to the URL; the account and search params are left as they are. */
  private applySelection(selection: TransactionFilterSelection): void {
    void this.router.navigate([], {
      queryParams: {
        from: selection.from,
        to: selection.to,
        type: selection.type,
        category: selection.categories.length > 0 ? selection.categories : null,
      },
      queryParamsHandling: 'merge',
    });
  }
}
