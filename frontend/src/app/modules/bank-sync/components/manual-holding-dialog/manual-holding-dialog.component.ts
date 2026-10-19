import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  ButtonComponent,
  CMN_DIALOG_DATA,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
  SelectComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {DateUtils} from '../../../../shared/utils/date.utils';
import {
  CONVERTIBILITY_OPTIONS,
  MANUAL_HOLDING_CURRENCY_OPTIONS,
  MANUAL_HOLDING_DEFAULT_CURRENCY,
  MANUAL_HOLDING_LABEL_MAX_LENGTH,
  MANUAL_HOLDING_MIN_AMOUNT,
} from '../../constants/manual-holding/manual-holding.constants';
import {
  type Convertibility,
  type ManualHoldingDialogData,
  type ManualHoldingInput,
} from '../../models/manual-holding/manual-holding.model';

@Component({
  selector: 'fns-manual-holding-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    InputHintsDirective,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
    SelectComponent,
  ],
  templateUrl: './manual-holding-dialog.component.html',
})
export class ManualHoldingDialogComponent {
  private readonly dialogRef = inject<DialogRef<ManualHoldingInput>>(DialogRef);
  private readonly holding = inject<ManualHoldingDialogData>(CMN_DIALOG_DATA).holding;

  public readonly currencyOptions = MANUAL_HOLDING_CURRENCY_OPTIONS;
  public readonly convertibilityOptions = CONVERTIBILITY_OPTIONS;
  public readonly isEdit = this.holding !== null;

  public readonly form = new FormGroup({
    label: new FormControl(this.holding?.label ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MANUAL_HOLDING_LABEL_MAX_LENGTH)],
    }),
    amount: new FormControl<number | null>(this.holding?.amount ?? null, {
      validators: [Validators.required, Validators.min(MANUAL_HOLDING_MIN_AMOUNT)],
    }),
    currency: new FormControl(this.holding?.currency ?? MANUAL_HOLDING_DEFAULT_CURRENCY, {
      nonNullable: true,
      validators: [Validators.required],
    }),
    convertibility: new FormControl<Convertibility>(this.holding?.convertibility ?? 'InEconomy', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  public submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({
      label: value.label.trim(),
      amount: Number(value.amount),
      currency: value.currency,
      // shortcut: a save records the value as of today; add an as-of field once @lifekit-hq/ui ships a single-date input.
      asOf: DateUtils.isoDateLocal(new Date()),
      convertibility: value.convertibility,
    });
  }

  public cancel(): void {
    this.dialogRef.close();
  }
}
