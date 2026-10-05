import {DialogRef} from '@angular/cdk/dialog';
import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {FormControl, FormGroup, ReactiveFormsModule, Validators} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  ChipComponent,
  CMN_DIALOG_DATA,
  DialogActionsComponent,
  EmptyStateComponent,
  FormFieldComponent,
  InputComponent,
  SelectableCardComponent,
} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {
  COMMITMENT_CADENCE_OPTIONS,
  COMMITMENT_KIND_OPTIONS,
  DEFAULT_COMMITMENT_CADENCE,
  MIN_TERM_COUNT,
} from '../../constants/commitment-candidate/commitment-candidate.constants';
import {MIN_MONTHLY_AMOUNT} from '../../constants/subscription/subscription-form.constants';
import {
  type CommitmentAnchor,
  type CommitmentCandidate,
} from '../../models/commitment-candidate/commitment-candidate.model';
import {type CommitmentDialogData} from '../../models/commitment-candidate/commitment-dialog.model';
import {
  type AddCommitmentRequest,
  type LinkCommitmentRequest,
  type SubscriptionCadence,
  type SubscriptionKind,
} from '../../models/subscription/subscription.model';
import {CommitmentPickerStore} from '../../store/commitment-picker/commitment-picker.store';

@Component({
  selector: 'fns-add-commitment-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    ButtonComponent,
    ChipComponent,
    DatePipe,
    DialogActionsComponent,
    EmptyStateComponent,
    FormFieldComponent,
    InputComponent,
    MoneyPipe,
    ReactiveFormsModule,
    SelectableCardComponent,
  ],
  providers: [CommitmentPickerStore],
  templateUrl: './add-commitment-dialog.component.html',
})
export class AddCommitmentDialogComponent {
  private readonly dialogRef =
    inject<DialogRef<AddCommitmentRequest | LinkCommitmentRequest>>(DialogRef);
  private readonly data = inject<Nullable<CommitmentDialogData>>(CMN_DIALOG_DATA, {optional: true});

  /** Linking an existing row only needs the transaction; the row keeps its own name and plan. */
  public readonly isLinking = (this.data?.linkTo ?? null) !== null;

  public readonly picker = inject(CommitmentPickerStore);
  public readonly kindOptions = COMMITMENT_KIND_OPTIONS;
  public readonly cadenceOptions = COMMITMENT_CADENCE_OPTIONS;
  public readonly searchControl = new FormControl('', {nonNullable: true});

  public readonly form = new FormGroup({
    kind: new FormControl<SubscriptionKind>(this.data?.kind ?? 'subscription', {
      nonNullable: true,
    }),
    cadence: new FormControl<SubscriptionCadence>(DEFAULT_COMMITMENT_CADENCE, {nonNullable: true}),
    transactionId: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    merchant: new FormControl('', {nonNullable: true, validators: [Validators.required]}),
    monthlyAmount: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(MIN_MONTHLY_AMOUNT)],
    }),
    termCount: new FormControl<number | null>(null, {
      validators: [Validators.min(MIN_TERM_COUNT)],
    }),
  });

  constructor() {
    this.picker.applySearch(toSignal(this.searchControl.valueChanges, {initialValue: ''}));
  }

  public setKind(kind: SubscriptionKind): void {
    this.form.controls.kind.setValue(kind);
    const transactionId = this.form.controls.transactionId.value;
    if (transactionId) {
      this.loadAnchor(transactionId);
    }
  }

  public setCadence(cadence: SubscriptionCadence): void {
    this.form.controls.cadence.setValue(cadence);
    this.form.controls.cadence.markAsDirty();
  }

  /** Picking a charge pre-fills the name and amount; both stay editable. */
  public pick(candidate: CommitmentCandidate): void {
    this.picker.select(candidate);
    this.form.patchValue({
      transactionId: candidate.transactionId,
      merchant: candidate.merchantName || candidate.description,
      monthlyAmount: candidate.amount,
      cadence: DEFAULT_COMMITMENT_CADENCE,
    });
    this.form.controls.monthlyAmount.markAsPristine();
    this.form.controls.cadence.markAsPristine();
    this.loadAnchor(candidate.transactionId);
  }

  public submit(): void {
    if (this.isLinking) {
      this.form.controls.transactionId.markAsTouched();
      if (this.form.controls.transactionId.valid) {
        this.dialogRef.close({
          transactionId: this.form.controls.transactionId.value,
          cadence: this.form.controls.cadence.value,
        });
      }
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const isInstallment = value.kind === 'installment';
    this.dialogRef.close({
      transactionId: value.transactionId,
      kind: value.kind,
      merchant: value.merchant.trim(),
      monthlyAmount: Number(value.monthlyAmount),
      termCount: isInstallment && value.termCount ? Number(value.termCount) : null,
      cadence: value.cadence,
    });
  }

  public cancel(): void {
    this.dialogRef.close();
  }

  private loadAnchor(transactionId: string): void {
    this.picker.loadAnchor({
      transactionId,
      kind: this.form.controls.kind.value,
      onLoaded: anchor => this.applyAnchor(anchor),
    });
  }

  private applyAnchor(anchor: CommitmentAnchor): void {
    if (!this.form.controls.monthlyAmount.dirty) {
      this.form.patchValue({monthlyAmount: anchor.amount});
    }
    if (anchor.cadence && !this.form.controls.cadence.dirty) {
      this.form.patchValue({cadence: anchor.cadence});
    }
  }
}
