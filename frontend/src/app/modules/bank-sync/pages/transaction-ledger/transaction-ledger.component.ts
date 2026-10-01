import {DatePipe, DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {ActivatedRoute, Router} from '@angular/router';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  CmnDrawerService,
  EmptyStateComponent,
  IconComponent,
  SkeletonComponent,
  StatCardComponent,
  TagComponent,
} from '@lifekit-hq/ui';
import {map} from 'rxjs';

import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {type GlobalTransactionDto} from '../../models/transaction/transaction.model';
import {TransactionAmountPipe} from '../../pipes/transaction-amount.pipe';
import {TransactionAmountClassPipe} from '../../pipes/transaction-amount-class.pipe';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';

const SKELETON_ROWS = 8;
const TYPE_LABELS: Record<string, string> = {debit: 'Spending', credit: 'Income'};

@Component({
  selector: 'fns-transaction-ledger',
  imports: [
    AlertComponent,
    TagComponent,
    ButtonComponent,
    CardComponent,
    DatePipe,
    DecimalPipe,
    EmptyStateComponent,
    IconComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    SkeletonComponent,
    StatCardComponent,
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

  public openDrawer(tx: GlobalTransactionDto): void {
    this.drawer.open(TransactionDrawerComponent, {
      title: tx.description,
      data: tx,
      width: '480px',
    });
  }

  public clearCategory(): void {
    void this.router.navigate([], {queryParams: {category: null}, queryParamsHandling: 'merge'});
  }

  public clearType(): void {
    void this.router.navigate([], {queryParams: {type: null}, queryParamsHandling: 'merge'});
  }
}
