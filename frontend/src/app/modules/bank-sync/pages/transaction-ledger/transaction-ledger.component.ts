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
import {type GlobalTransactionDto} from '../../models/transaction/transaction.model';
import {TransactionAmountPipe} from '../../pipes/transaction-amount.pipe';
import {TransactionAmountClassPipe} from '../../pipes/transaction-amount-class.pipe';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';

const SKELETON_ROWS = 8;
const DRAWER_WIDTH = '480px';
const TYPE_LABELS: Record<string, string> = {debit: 'Spending', credit: 'Income'};

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

  public readonly activeType = toSignal(this.route.queryParamMap.pipe(map(p => p.get('type'))), {
    initialValue: null,
  });

  public readonly activeCategoryLabel = computed(() => {
    const cat = this.activeCategory();
    return cat ? MerchantCategoryUtils.format(cat) : null;
  });

  public readonly activeTypeLabel = computed(() => {
    const type = this.activeType();
    return type ? (TYPE_LABELS[type] ?? type) : null;
  });

  public readonly displayedTransactions = computed(() => {
    const cat = this.activeCategory();
    const type = this.activeType();
    let all = this.store.transactions();

    if (cat) {
      const target = cat.toLowerCase();
      all = all.filter(t => t.merchantCategory?.toLowerCase() === target);
    }
    if (type) {
      all = all.filter(t => t.transactionType === type);
    }
    return all;
  });

  public readonly dayGroups = computed(() =>
    TransactionGroupUtils.groupByDay(this.displayedTransactions())
  );

  constructor() {
    this.store.applyAccount(this.activeAccount);
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

  public clearType(): void {
    void this.router.navigate([], {queryParams: {type: null}, queryParamsHandling: 'merge'});
  }
}
