import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {takeUntilDestroyed, toObservable} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {Router} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
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

import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
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
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';

const SKELETON_ROWS = 8;
const DRAWER_WIDTH = '480px';

@Component({
  selector: 'fns-transaction-ledger',
  imports: [
    PageContainerComponent,
    AlertComponent,
    AsyncStateComponent,
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
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(TransactionLedgerStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});

  public readonly searchControl = new FormControl(this.store.search(), {nonNullable: true});

  public readonly periods = LEDGER_PERIODS;

  /** The quick period whose bounds equal the date filter; any other range selects none. */
  public readonly view = computed(() =>
    AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData: this.store.transactions().length > 0,
      errorMessage: this.store.errorMessage(),
      offline: this.connectivity.offline(),
      lastSyncedAt: this.store.lastSyncedAt(),
    })
  );
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

  public readonly activeCategoryLabel = computed(() => {
    const cat = this.store.category();
    return cat ? MerchantCategoryUtils.format(cat) : null;
  });

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
