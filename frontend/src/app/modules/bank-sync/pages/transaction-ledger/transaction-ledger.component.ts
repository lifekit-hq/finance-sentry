import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
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
  SkeletonComponent,
} from '@lifekit-hq/ui';
import {map} from 'rxjs';

import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {
  type GlobalTransactionDto,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {TransactionAmountPipe} from '../../pipes/transaction-amount.pipe';
import {TransactionAmountClassPipe} from '../../pipes/transaction-amount-class.pipe';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
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
  public readonly searchControl = new FormControl('', {nonNullable: true});

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
    this.store.applySearch(toSignal(this.searchControl.valueChanges, {initialValue: ''}));
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
