import {DatePipe, SlicePipe, UpperCasePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject, signal, ViewContainerRef} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  ButtonComponent,
  CardComponent,
  ChipComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  FormFieldComponent,
  InputComponent,
  LineChartComponent,
  ListItemRowComponent,
  PageHeaderComponent,
  StatCardComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {
  type Subscription,
  type SubscriptionSort,
} from '../../models/subscription/subscription.model';
import {MerchantColorPipe} from '../../pipes/merchant-color.pipe';
import {SubscriptionsStore} from '../../store/subscriptions/subscriptions.store';

const MS_PER_DAY = 86_400_000;
const MIN_MONTHLY_AMOUNT = 0.01;

const SORT_OPTIONS: {value: SubscriptionSort; label: string}[] = [
  {value: 'date', label: 'Next charge'},
  {value: 'amount', label: 'Amount'},
  {value: 'name', label: 'Name'},
];

@Component({
  selector: 'fns-subscriptions',
  imports: [
    FormFieldComponent,
    MoneyPipe,
    ButtonComponent,
    CardComponent,
    ChipComponent,
    DatePipe,
    InputComponent,
    LineChartComponent,
    ListItemRowComponent,
    MerchantColorPipe,
    PageHeaderComponent,
    ReactiveFormsModule,
    SlicePipe,
    StatCardComponent,
    UpperCasePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [SubscriptionsStore],
  templateUrl: './subscriptions.component.html',
})
export class SubscriptionsComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  // One reusable control for the inline "set term" editor per installment row.
  private readonly termControls = new Map<string, FormControl<number | null>>();

  public readonly store = inject(SubscriptionsStore);
  public readonly sortOptions = SORT_OPTIONS;

  public readonly showAddForm = signal(false);
  public readonly showSubForm = signal(false);

  public readonly subForm = new FormGroup({
    merchant: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    monthlyAmount: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(MIN_MONTHLY_AMOUNT)],
    }),
    currency: new FormControl('EUR', {nonNullable: true, validators: [Validators.required]}),
    startDate: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
  });

  public readonly addForm = new FormGroup({
    merchant: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    monthlyAmount: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(MIN_MONTHLY_AMOUNT)],
    }),
    currency: new FormControl('UAH', {nonNullable: true, validators: [Validators.required]}),
    startDate: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    termCount: new FormControl<number | null>(null),
  });

  public daysUntil(dateStr: string): number {
    return Math.ceil((new Date(dateStr).getTime() - Date.now()) / MS_PER_DAY);
  }

  public setSort(sort: SubscriptionSort): void {
    this.store.setSort(sort);
  }

  public dismiss(sub: Pick<Subscription, 'id' | 'merchantName'>): void {
    const ref = this.dialog.open<boolean>(ConfirmDialogComponent, {
      data: {
        title: `Dismiss ${sub.merchantName}?`,
        message: `This will hide ${sub.merchantName} from your subscriptions. It will survive future detection runs.`,
        confirmLabel: 'Dismiss',
        cancelLabel: 'Keep',
        confirmVariant: 'destructive',
      },
      size: 'sm',
      viewContainerRef: this.viewContainerRef,
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.dismiss(sub.id);
        }
      });
  }

  public restore(id: string): void {
    this.store.restore(id);
  }

  public markInstallmentDone(id: string): void {
    this.store.completeInstallment(id);
  }

  public deleteInstallment(sub: Pick<Subscription, 'id' | 'merchantName'>): void {
    const ref = this.dialog.open<boolean>(ConfirmDialogComponent, {
      data: {
        title: `Delete ${sub.merchantName}?`,
        message: 'Permanently remove this installment.',
        confirmLabel: 'Delete',
        cancelLabel: 'Keep',
        confirmVariant: 'destructive',
      },
      size: 'sm',
      viewContainerRef: this.viewContainerRef,
    });
    ref
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          this.store.deleteInstallment(sub.id);
        }
      });
  }

  public termControl(sub: Pick<Subscription, 'id' | 'termCount'>): FormControl<number | null> {
    let control = this.termControls.get(sub.id);
    if (!control) {
      control = new FormControl<number | null>(sub.termCount ?? null);
      this.termControls.set(sub.id, control);
    }
    return control;
  }

  public saveTerm(id: string): void {
    const control = this.termControls.get(id);
    const value = control?.value ?? null;
    this.store.setInstallmentTerm({id, termCount: value && value > 0 ? value : null});
  }

  public toggleAddForm(): void {
    this.showAddForm.update(open => !open);
  }

  public toggleSubForm(): void {
    this.showSubForm.update(open => !open);
  }

  public submitSubscription(): void {
    if (this.subForm.invalid) {
      this.subForm.markAllAsTouched();
      return;
    }
    const value = this.subForm.getRawValue();
    this.store.addSubscription({
      merchant: value.merchant,
      monthlyAmount: value.monthlyAmount ?? 0,
      currency: value.currency,
      startDate: value.startDate,
    });
    this.subForm.reset({merchant: '', monthlyAmount: null, currency: 'EUR', startDate: ''});
    this.showSubForm.set(false);
  }

  public submitAdd(): void {
    if (this.addForm.invalid) {
      this.addForm.markAllAsTouched();
      return;
    }
    const value = this.addForm.getRawValue();
    this.store.addInstallment({
      merchant: value.merchant,
      monthlyAmount: value.monthlyAmount ?? 0,
      currency: value.currency,
      startDate: value.startDate,
      termCount: value.termCount && value.termCount > 0 ? value.termCount : null,
    });
    this.addForm.reset({
      merchant: '',
      monthlyAmount: null,
      currency: 'UAH',
      startDate: '',
      termCount: null,
    });
    this.showAddForm.set(false);
  }
}
