import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {FormControl, FormGroup, ReactiveFormsModule} from '@angular/forms';
import {
  AlertComponent,
  ButtonComponent,
  CMN_DIALOG_DATA,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {PROVIDER_KEYS_TO_REVOKE} from '../../constants/account-closure/account-closure.constants';
import {type CloseAccountDialogData} from '../../models/account-closure/account-closure.model';
import {AccountClosureUtils} from '../../utils/account-closure.utils';

/** Closes with the typed email once it matches the account's, or with nothing when cancelled. */
@Component({
  selector: 'fns-close-account-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    InputHintsDirective,
    ReactiveFormsModule,
  ],
  templateUrl: './close-account-dialog.component.html',
})
export class CloseAccountDialogComponent {
  private readonly dialogRef = inject<DialogRef<string>>(DialogRef);

  public readonly data = inject<CloseAccountDialogData>(CMN_DIALOG_DATA);
  public readonly providerKeys = PROVIDER_KEYS_TO_REVOKE;
  public readonly control = new FormControl('', {nonNullable: true});
  // A bound group, so the submit button (or Enter) raises ngSubmit instead of a native page reload.
  public readonly form = new FormGroup({email: this.control});

  public readonly typed = toSignal(this.control.valueChanges, {initialValue: ''});
  public readonly confirmed = computed(() =>
    AccountClosureUtils.confirms(this.typed(), this.data.email)
  );

  public close(): void {
    if (this.confirmed()) {
      this.dialogRef.close(this.control.value.trim());
    }
  }

  public cancel(): void {
    this.dialogRef.close();
  }
}
