import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnDrawerService,
  type DateRange,
  DateRangeComponent,
  EmptyStateComponent,
  InputComponent,
  InstitutionAvatarComponent,
  ListItemRowComponent,
  MultiSelectComponent,
  SearchInputComponent,
  type SelectOptionValue,
  SkeletonComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {InstitutionLogoPipe} from '../../../../shared/pipes/institution-logo.pipe';
import {MerchantCategoryPipe} from '../../../../shared/pipes/merchant-category.pipe';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {TransactionDrawerComponent} from '../../components/transaction-drawer/transaction-drawer.component';
import {
  type GlobalTransactionDto,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {TransactionAmountPipe} from '../../pipes/transaction-amount.pipe';
import {TransactionAmountClassPipe} from '../../pipes/transaction-amount-class.pipe';
import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';

const SKELETON_ROWS = 8;
const DRAWER_WIDTH = '480px';

@Component({
  selector: 'fns-transaction-ledger',
  imports: [
    InputHintsDirective,
    AlertComponent,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    DateRangeComponent,
    EmptyStateComponent,
    FormsModule,
    InputComponent,
    InstitutionAvatarComponent,
    InstitutionLogoPipe,
    ListItemRowComponent,
    MerchantCategoryPipe,
    MoneyPipe,
    MultiSelectComponent,
    SearchInputComponent,
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

  public readonly store = inject(TransactionLedgerStore);
  public readonly categories = inject(CategoryStore);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});

  public openDrawer(tx: GlobalTransactionDto): void {
    this.drawer.open(TransactionDrawerComponent, {
      title: tx.description,
      data: tx,
      width: DRAWER_WIDTH,
    });
  }

  public selectType(transactionType: Nullable<TransactionType>): void {
    this.store.setFilters({transactionType});
  }

  public selectAccounts(values: SelectOptionValue[]): void {
    this.store.setFilters({accountIds: values.map(String)});
  }

  public selectCategories(values: SelectOptionValue[]): void {
    this.store.setFilters({categories: values.map(String)});
  }

  public selectDateRange(range: Nullable<DateRange>): void {
    this.store.setFilters({from: range?.from || null, to: range?.to || null});
  }
}
