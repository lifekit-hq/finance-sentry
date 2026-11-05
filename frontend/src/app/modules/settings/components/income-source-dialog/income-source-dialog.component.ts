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

import {ENTRY_CURRENCY_OPTIONS} from '../../../../shared/constants/money/money.constants';
import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {DateUtils} from '../../../../shared/utils/date.utils';
import {
  INCOME_BASIS_OPTIONS,
  INCOME_CADENCE_OPTIONS,
  INCOME_KIND_OPTIONS,
  INCOME_SOURCE_DEFAULT_CURRENCY,
  INCOME_SOURCE_LABEL_MAX_LENGTH,
  INCOME_SOURCE_MIN_AMOUNT,
} from '../../constants/income-source/income-source.constants';
import {
  type IncomeBasis,
  type IncomeCadence,
  type IncomeKind,
  type IncomeSourceDialogData,
  type IncomeSourceInput,
} from '../../models/income-source/income-source.model';

@Component({
  selector: 'fns-income-source-dialog',
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
  templateUrl: './income-source-dialog.component.html',
})
export class IncomeSourceDialogComponent {
  private readonly dialogRef = inject<DialogRef<IncomeSourceInput>>(DialogRef);
  private readonly source = inject<IncomeSourceDialogData>(CMN_DIALOG_DATA).source;

  public readonly kindOptions = INCOME_KIND_OPTIONS;
  public readonly cadenceOptions = INCOME_CADENCE_OPTIONS;
  public readonly basisOptions = INCOME_BASIS_OPTIONS;
  public readonly currencyOptions = ENTRY_CURRENCY_OPTIONS;
  public readonly isEdit = this.source !== null;

  public readonly form = new FormGroup({
    label: new FormControl(this.source?.label ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(INCOME_SOURCE_LABEL_MAX_LENGTH)],
    }),
    kind: new FormControl<IncomeKind>(this.source?.kind ?? 'Salary', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    amount: new FormControl<number | null>(this.source?.amount ?? null, {
      validators: [Validators.required, Validators.min(INCOME_SOURCE_MIN_AMOUNT)],
    }),
    currency: new FormControl(this.source?.currency ?? INCOME_SOURCE_DEFAULT_CURRENCY, {
      nonNullable: true,
      validators: [Validators.required],
    }),
    cadence: new FormControl<IncomeCadence>(this.source?.cadence ?? 'Monthly', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    grossOrNet: new FormControl<IncomeBasis>(this.source?.grossOrNet ?? 'Net', {
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
      kind: value.kind,
      label: value.label.trim(),
      amount: Number(value.amount),
      currency: value.currency,
      cadence: value.cadence,
      grossOrNet: value.grossOrNet,
      // shortcut: a new source starts today and an edit keeps its dates; add date fields once @lifekit-hq/ui ships a single-date input. Retire ends a source.
      startDate: this.source?.startDate ?? DateUtils.isoDateLocal(new Date()),
      endDate: this.source?.endDate ?? null,
    });
  }

  public cancel(): void {
    this.dialogRef.close();
  }
}
