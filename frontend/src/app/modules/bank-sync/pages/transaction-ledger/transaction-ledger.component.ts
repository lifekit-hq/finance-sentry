import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {takeUntilDestroyed, toSignal} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {ActivatedRoute, Router} from '@angular/router';
import {
  AsyncStateComponent,
  type AsyncStateStatus,
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnDrawerService,
  EmptyStateComponent,
  InstitutionAvatarComponent,
  ListItemRowComponent,
  PageContainerComponent,
  SearchInputComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';
import {debounceTime, distinctUntilChanged, map} from 'rxjs';

import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {TransactionFilterSheetComponent} from '../../components/transaction-filter-sheet/transaction-filter-sheet.component';
import {LEDGER_PERIODS} from '../../constants/ledger-period/ledger-period.constants';
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
import {SEARCH_DEBOUNCE_MS} from '../../store/transaction-ledger/transaction-ledger.effects';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';

const SKELETON_ROWS = 8;
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

function isoDateOrNull(value: string | null): Nullable<string> {
  return value !== null && ISO_DATE.test(value) ? value : null;
}
const DRAWER_WIDTH = '480px';
const FILTER_SHEET_WIDTH = '420px';

@Component({
  selector: 'fns-transaction-ledger',
  imports: [
    PageContainerComponent,
    AsyncStateComponent,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    EmptyStateComponent,
    InstitutionAvatarComponent,
    InstitutionLogoPipe,
    ListItemRowComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    ReactiveFormsModule,
    SearchInputComponent,
    SkeletonComponent,
    TransactionAmountClassPipe,
    TransactionAmountPipe,
  ],
  templateUrl: './transaction-ledger.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  host: {class: 'block h-full'},
  providers: [TransactionLedgerStore],
})
export class TransactionLedgerComponent {
  private readonly categoryStore = inject(CategoryStore);
  private readonly drawer = inject(CmnDrawerService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  public readonly store = inject(TransactionLedgerStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});

  /** The search text in the `q` query param: a shared link or back/forward restores the search. */
  public readonly activeSearch = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('q') ?? '')),
    {initialValue: ''}
  );

  public readonly searchControl = new FormControl(this.activeSearch(), {nonNullable: true});

  public readonly activeAccount = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('account'))),
    {initialValue: null}
  );

  public readonly activeCategories = toSignal(
    this.route.queryParamMap.pipe(map(p => p.getAll('category').filter(c => c !== ''))),
    {initialValue: []}
  );

  public readonly activeType = toSignal(
    this.route.queryParamMap.pipe(
      map((p): Nullable<TransactionType> => {
        const type = p.get('type');
        return type === 'credit' || type === 'debit' ? type : null;
      })
    ),
    {initialValue: null}
  );

  public readonly activeDateRange = toSignal(
    this.route.queryParamMap.pipe(
      map(p => ({from: isoDateOrNull(p.get('from')), to: isoDateOrNull(p.get('to'))}))
    ),
    {initialValue: {from: null, to: null}}
  );

  public readonly periods = LEDGER_PERIODS;

  /** The quick period whose bounds equal the date filter; any other range selects none. */
  public readonly ledgerStatus = computed<AsyncStateStatus>(() => {
    if (this.store.isLoading()) {
      return 'loading';
    }
    return this.store.errorMessage() ? 'error' : 'success';
  });
  public readonly activePeriod = computed(() => {
    const {from, to} = this.activeDateRange();
    return LedgerPeriodUtils.match(from, to);
  });

  public readonly activeDateRangeLabel = computed(() => {
    const {from, to} = this.activeDateRange();
    if (!from && !to) {
      return null;
    }
    return from && to ? `${from} – ${to}` : from ? `from ${from}` : `until ${to}`;
  });

  /** Filters the sheet edits, as they stand in the URL. */
  public readonly activeSelection = computed<TransactionFilterSelection>(() => ({
    ...this.activeDateRange(),
    type: this.activeType(),
    categories: this.activeCategories(),
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
    const type = this.activeType();
    if (type) {
      filters.push({
        id: 'type',
        label: `Type: ${type === 'credit' ? 'In' : 'Out'}`,
        remove: () => this.selectType(null),
      });
    }
    for (const key of this.activeCategories()) {
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
    this.store.applyAccount(this.activeAccount);
    this.store.applyType(this.activeType);
    this.store.applyCategories(this.activeCategories);
    this.store.applyDateRange(this.activeDateRange);
    this.store.applySearch(
      toSignal(this.searchControl.valueChanges, {initialValue: this.searchControl.value})
    );
    this.followSearchParam();
    this.syncSearchParam();
  }

  /** URL → box: back/forward (or a new deep link) rewrites the box when it differs. */
  private followSearchParam(): void {
    this.route.queryParamMap
      .pipe(
        map(p => p.get('q') ?? ''),
        takeUntilDestroyed()
      )
      .subscribe(q => {
        if (q !== this.searchControl.value) {
          this.searchControl.setValue(q);
        }
      });
  }

  /** Box → URL: debounced, replacing the history entry, merged with the other filters. */
  private syncSearchParam(): void {
    this.searchControl.valueChanges
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe(search => {
        if (search.trim() === this.activeSearch().trim()) {
          return;
        }
        void this.router.navigate([], {
          queryParams: {q: search.trim() === '' ? null : search},
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
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
      categories: this.activeCategories().filter(c => c !== key),
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
