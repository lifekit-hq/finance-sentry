import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  type OnInit,
  output,
  signal,
} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {
  ButtonComponent,
  ChipComponent,
  type DateRange,
  DateRangeComponent,
  type Nullable,
} from '@lifekit-hq/ui';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {LEDGER_PERIODS} from '../../constants/ledger-period/ledger-period.constants';
import {type LedgerPeriod} from '../../models/ledger-period/ledger-period.model';
import {
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {
  EMPTY_TRANSACTION_FILTER,
  type TransactionFilterSelection,
} from '../../models/transaction/transaction-filter.model';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';

/**
 * Body of the ledger's filter sheet (Period / Type / Category / Account), rendered by `cmn-filter-bar`. It
 * edits a draft copy of `initial` and emits it on "Show results"; closing the sheet any other way
 * drops it.
 */
@Component({
  selector: 'fns-transaction-filter-sheet',
  imports: [ButtonComponent, ChipComponent, DateRangeComponent, ReactiveFormsModule],
  templateUrl: './transaction-filter-sheet.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TransactionFilterSheetComponent implements OnInit {
  public readonly initial = input.required<TransactionFilterSelection>();
  /** Accounts the Account section offers; it is hidden unless there is more than one. */
  public readonly accounts = input<TransactionAccountOption[]>([]);
  public readonly applied = output<TransactionFilterSelection>();

  public readonly categoryStore = inject(CategoryStore);
  public readonly periods = LEDGER_PERIODS;

  public readonly draft = linkedSignal<TransactionFilterSelection>(() => this.initial());
  public readonly customOpen = signal(false);
  public readonly rangeControl = new FormControl<DateRange>(
    {from: null, to: null},
    {nonNullable: true}
  );

  public readonly activePeriod = computed(() =>
    LedgerPeriodUtils.match(this.draft().from, this.draft().to)
  );
  /** A range no quick period matches is a custom one, so its dates stay editable. */
  public readonly isCustom = computed(() => {
    const {from, to} = this.draft();
    return this.customOpen() || (this.activePeriod() === null && (from !== null || to !== null));
  });
  public readonly rangeInverted = computed(() => {
    const {from, to} = this.draft();
    return from !== null && to !== null && from > to;
  });

  constructor() {
    this.rangeControl.valueChanges.pipe(takeUntilDestroyed()).subscribe(range => {
      this.draft.update(d => ({...d, from: range.from || null, to: range.to || null}));
    });
  }

  public ngOnInit(): void {
    const {from, to} = this.draft();
    this.rangeControl.setValue({from, to}, {emitEvent: false});
  }

  public selectPeriod(period: LedgerPeriod): void {
    const {from, to} = LedgerPeriodUtils.dates(period);
    this.customOpen.set(false);
    this.rangeControl.setValue({from, to}, {emitEvent: false});
    this.draft.update(d => ({...d, from, to}));
  }

  public openCustom(): void {
    this.customOpen.set(true);
  }

  public selectType(type: Nullable<TransactionType>): void {
    this.draft.update(d => ({...d, type}));
  }

  public selectAccount(accountId: Nullable<string>): void {
    this.draft.update(d => ({...d, accountId}));
  }

  public toggleCategory(key: string): void {
    this.draft.update(d => ({
      ...d,
      categories: d.categories.includes(key)
        ? d.categories.filter(c => c !== key)
        : [...d.categories, key],
    }));
  }

  public reset(): void {
    this.customOpen.set(false);
    this.rangeControl.setValue({from: null, to: null}, {emitEvent: false});
    this.draft.set(EMPTY_TRANSACTION_FILTER);
  }

  public apply(): void {
    this.applied.emit(this.draft());
  }
}
