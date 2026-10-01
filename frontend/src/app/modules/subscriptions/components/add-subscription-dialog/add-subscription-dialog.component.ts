import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  ButtonComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {
  DEFAULT_SUBSCRIPTION_CURRENCY,
  MIN_MONTHLY_AMOUNT,
  START_DATE_PATTERN,
} from '../../constants/subscription/subscription-form.constants';
import {type AddSubscriptionRequest} from '../../models/subscription/subscription.model';

@Component({
  selector: 'fns-add-subscription-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
  ],
  templateUrl: './add-subscription-dialog.component.html',
})
export class AddSubscriptionDialogComponent {
  private readonly dialogRef = inject<DialogRef<AddSubscriptionRequest>>(DialogRef);

  public readonly form = new FormGroup({
    merchant: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    monthlyAmount: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(MIN_MONTHLY_AMOUNT)],
    }),
    currency: new FormControl(DEFAULT_SUBSCRIPTION_CURRENCY, {
      nonNullable: true,
      validators: [Validators.required],
    }),
    startDate: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(START_DATE_PATTERN)],
    }),
  });

  public submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({
      merchant: value.merchant,
      monthlyAmount: Number(value.monthlyAmount),
      currency: value.currency,
      startDate: value.startDate,
    });
  }

  public cancel(): void {
    this.dialogRef.close();
  }
}
