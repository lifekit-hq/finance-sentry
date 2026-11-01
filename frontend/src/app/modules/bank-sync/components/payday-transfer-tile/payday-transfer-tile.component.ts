import {DatePipe, DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {FormControl, FormGroup, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  CardComponent,
  FormFieldComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {PaydayTransferStore} from '../../store/payday-transfer/payday-transfer.store';
import {amountText} from '../../validators/amount-text.validator';
import {
  MIN_OUTFLOW_UAH,
  MIN_STEP_EUR,
  OUTFLOW_ERROR,
  RATE_DIGITS_FORMAT,
  STEP_ERROR,
} from './payday-transfer-tile.constants';

@Component({
  selector: 'fns-payday-transfer-tile',
  imports: [
    AlertComponent,
    ButtonComponent,
    CardComponent,
    DatePipe,
    DecimalPipe,
    FormFieldComponent,
    InputComponent,
    MoneyPipe,
    ReactiveFormsModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [PaydayTransferStore],
  templateUrl: './payday-transfer-tile.component.html',
})
export class PaydayTransferTileComponent {
  public readonly store = inject(PaydayTransferStore);
  public readonly outflowError = OUTFLOW_ERROR;
  public readonly stepError = STEP_ERROR;
  public readonly rateFormat = RATE_DIGITS_FORMAT;

  public readonly form = new FormGroup({
    fixedOutflowUah: new FormControl('', {
      nonNullable: true,
      validators: [amountText(MIN_OUTFLOW_UAH)],
    }),
    roundingStepEur: new FormControl('', {
      nonNullable: true,
      validators: [amountText(MIN_STEP_EUR)],
    }),
  });

  public edit(): void {
    const transfer = this.store.transfer();
    if (!transfer) {
      return;
    }
    this.form.setValue({
      fixedOutflowUah: String(transfer.fixedOutflowUah),
      roundingStepEur: String(transfer.roundingStepEur),
    });
    this.store.setEditing(true);
  }

  public cancel(): void {
    this.store.setEditing(false);
  }

  public save(): void {
    if (this.form.invalid) {
      return;
    }
    const {fixedOutflowUah, roundingStepEur} = this.form.getRawValue();
    this.store.saveSettings({
      fixedOutflowUah: Number(fixedOutflowUah),
      roundingStepEur: Number(roundingStepEur),
    });
  }
}
