import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {takeUntilDestroyed, toSignal} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {ActivatedRoute, Router} from '@angular/router';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnDrawerService,
  EmptyStateComponent,
  IconComponent,
  InputComponent,
  InstitutionAvatarComponent,
  ListItemRowComponent,
  PageContainerComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';
import {debounceTime, distinctUntilChanged, map} from 'rxjs';

import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {LEDGER_PERIODS} from '../../constants/ledger-period/ledger-period.constants';
import {type LedgerPeriod} from '../../models/ledger-period/ledger-period.model';
import {
  type GlobalTransactionDto,
  type TransactionType,
} from '../../models/transaction/transaction.model';
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

@Component({
  selector: 'fns-transaction-ledger',
  imports: [
    PageContainerComponent,
    AlertComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    EmptyStateComponent,
    IconComponent,
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
  host: {class: 'block h-full'},
  providers: [TransactionLedgerStore],
})
export class TransactionLedgerComponent {
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

  public readonly activeCategory = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('category'))),
    {initialValue: null}
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

  public readonly activeCategoryLabel = computed(() => {
    const cat = this.activeCategory();
    return cat ? MerchantCategoryUtils.format(cat) : null;
  });

  public readonly dayGroups = computed(() =>
    TransactionGroupUtils.groupByDay(this.store.transactions())
  );

  constructor() {
    this.store.applyAccount(this.activeAccount);
    this.store.applyType(this.activeType);
    this.store.applyCategory(this.activeCategory);
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

  public clearCategory(): void {
    void this.router.navigate([], {queryParams: {category: null}, queryParamsHandling: 'merge'});
  }

  public selectPeriod(period: LedgerPeriod): void {
    void this.router.navigate([], {
      queryParams: LedgerPeriodUtils.dates(period),
      queryParamsHandling: 'merge',
    });
  }

  public clearDateRange(): void {
    void this.router.navigate([], {
      queryParams: {from: null, to: null},
      queryParamsHandling: 'merge',
    });
  }

  public selectType(type: Nullable<TransactionType>): void {
    void this.router.navigate([], {queryParams: {type}, queryParamsHandling: 'merge'});
  }
}
