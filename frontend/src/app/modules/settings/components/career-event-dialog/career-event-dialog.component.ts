import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  ButtonComponent,
  CMN_DIALOG_DATA,
  type DateRange,
  DateRangeComponent,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
  SelectComponent,
  TextareaComponent,
} from '@lifekit-hq/ui';
import {tap} from 'rxjs';

import {ENTRY_CURRENCY_OPTIONS} from '../../../../shared/constants/money/money.constants';
import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {
  CAREER_EVENT_AMOUNT_HINT,
  CAREER_EVENT_DEFAULT_CURRENCY,
  CAREER_EVENT_KIND_OPTIONS,
  CAREER_EVENT_LABEL_MAX_LENGTH,
  CAREER_EVENT_NOTE_MAX_LENGTH,
  CAREER_EVENT_NOTE_ROWS,
} from '../../constants/career-event/career-event.constants';
import {
  type CareerEventDialogData,
  type CareerEventInput,
  type CareerEventKind,
} from '../../models/career-event/career-event.model';
import {careerEventAmount} from '../../validators/career-event-amount.validator';
import {careerEventWhen} from '../../validators/career-event-when.validator';

@Component({
  selector: 'fns-career-event-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    InputHintsDirective,
    ButtonComponent,
    DateRangeComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
    SelectComponent,
    TextareaComponent,
  ],
  templateUrl: './career-event-dialog.component.html',
})
export class CareerEventDialogComponent {
  private readonly dialogRef = inject<DialogRef<CareerEventInput>>(DialogRef);
  private readonly event = inject<CareerEventDialogData>(CMN_DIALOG_DATA).event;
  // The validators run while the form is still being built, so they read this, not the form's own kind control.
  private currentKind: CareerEventKind = this.event?.kind ?? 'IncomeStep';

  public readonly kindOptions = CAREER_EVENT_KIND_OPTIONS;
  public readonly currencyOptions = ENTRY_CURRENCY_OPTIONS;
  public readonly isEdit = this.event !== null;

  public readonly form = new FormGroup({
    kind: new FormControl<CareerEventKind>(this.event?.kind ?? 'IncomeStep', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    label: new FormControl(this.event?.label ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(CAREER_EVENT_LABEL_MAX_LENGTH)],
    }),
    amount: new FormControl<number | null>(this.event?.amount ?? null, {
      validators: [careerEventAmount(() => this.currentKind)],
    }),
    currency: new FormControl(this.event?.currency ?? CAREER_EVENT_DEFAULT_CURRENCY, {
      nonNullable: true,
      validators: [Validators.required],
    }),
    when: new FormControl<DateRange>(
      {from: this.event?.date ?? null, to: this.event?.endDate ?? null},
      {nonNullable: true, validators: [careerEventWhen(() => this.currentKind)]}
    ),
    note: new FormControl(this.event?.note ?? '', {
      nonNullable: true,
      validators: [Validators.maxLength(CAREER_EVENT_NOTE_MAX_LENGTH)],
    }),
  });

  // The kind decides which sign and which dates are valid, so a change re-checks the fields that depend on it.
  public readonly kindValue = toSignal(
    this.form.controls.kind.valueChanges.pipe(
      tap(kind => {
        this.currentKind = kind;
        this.form.controls.amount.updateValueAndValidity();
        this.form.controls.when.updateValueAndValidity();
      })
    ),
    {initialValue: this.form.controls.kind.value}
  );

  public readonly noteRows = CAREER_EVENT_NOTE_ROWS;
  public readonly isLeave = computed(() => this.kindValue() === 'Leave');
  public readonly amountHint = computed(() => CAREER_EVENT_AMOUNT_HINT.get(this.kindValue()) ?? '');

  public submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const note = value.note.trim();
    this.dialogRef.close({
      kind: value.kind,
      date: value.when.from ?? '',
      endDate: value.kind === 'Leave' ? value.when.to : null,
      label: value.label.trim(),
      amount: Number(value.amount),
      currency: value.currency,
      note: note === '' ? null : note,
    });
  }

  public cancel(): void {
    this.dialogRef.close();
  }
}
