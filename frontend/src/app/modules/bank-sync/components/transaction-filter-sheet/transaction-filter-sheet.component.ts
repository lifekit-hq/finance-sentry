import {ChangeDetectionStrategy, Component, computed, inject, signal} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {
  ButtonComponent,
  ChipComponent,
  CMN_DRAWER_DATA,
  CmnDrawerRef,
  type DateRange,
  DateRangeComponent,
} from '@lifekit-hq/ui';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {LEDGER_PERIODS} from '../../constants/ledger-period/ledger-period.constants';
import {type LedgerPeriod} from '../../models/ledger-period/ledger-period.model';
import {type TransactionType} from '../../models/transaction/transaction.model';
import {
  EMPTY_TRANSACTION_FILTER,
  type TransactionFilterSelection,
} from '../../models/transaction/transaction-filter.model';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';

/**
 * Body of the ledger's filter sheet (Period / Type / Category). It edits a draft copy of the
 * page's filters and hands it back on "Show results"; closing the sheet any other way drops it.
 */
@Component({
  selector: 'fns-transaction-filter-sheet',
  imports: [ButtonComponent, ChipComponent, DateRangeComponent, ReactiveFormsModule],
  templateUrl: './transaction-filter-sheet.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TransactionFilterSheetComponent {
  private readonly drawerRef = inject<CmnDrawerRef<TransactionFilterSelection>>(CmnDrawerRef);

  public readonly categoryStore = inject(CategoryStore);
  public readonly periods = LEDGER_PERIODS;

  public readonly draft = signal<TransactionFilterSelection>(
    inject<TransactionFilterSelection>(CMN_DRAWER_DATA)
  );
  public readonly customOpen = signal(false);
  public readonly rangeControl = new FormControl<DateRange>(
    {from: this.draft().from, to: this.draft().to},
    {nonNullable: true}
  );

  public readonly activePeriod = computed(() =>
    LedgerPeriodUtils.match(this.draft().from, this.draft().to)
  );
  /** A range no quick period matches is a custom one, so its dates stay editable. */
  public readonly isCustom = computed(() => this.customOpen() || this.activePeriod() === null);
  public readonly rangeInverted = computed(() => {
    const {from, to} = this.draft();
    return from !== null && to !== null && from > to;
  });

  constructor() {
    this.rangeControl.valueChanges.pipe(takeUntilDestroyed()).subscribe(range => {
      this.draft.update(d => ({...d, from: range.from || null, to: range.to || null}));
    });
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
    this.drawerRef.close(this.draft());
  }
}
