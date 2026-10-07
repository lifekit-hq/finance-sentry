import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {FormControl, ReactiveFormsModule} from '@angular/forms';
import {
  ButtonComponent,
  CMN_DIALOG_DATA,
  DialogActionsComponent,
  FormFieldComponent,
  InputComponent,
} from '@lifekit-hq/ui';

import {InputHintsDirective} from '../../../../shared/directives/input-hints.directive';
import {type SetTermDialogData} from '../../models/subscription/set-term-dialog.model';

@Component({
  selector: 'fns-set-term-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    InputHintsDirective,
    ButtonComponent,
    DialogActionsComponent,
    FormFieldComponent,
    InputComponent,
    ReactiveFormsModule,
  ],
  templateUrl: './set-term-dialog.component.html',
})
export class SetTermDialogComponent {
  private readonly dialogRef = inject<DialogRef<Nullable<number>>>(DialogRef);

  public readonly data = inject<SetTermDialogData>(CMN_DIALOG_DATA);
  public readonly control = new FormControl<number | null>(this.data.termCount);

  public save(): void {
    const value = Number(this.control.value);
    this.dialogRef.close(value > 0 ? value : null);
  }

  public cancel(): void {
    this.dialogRef.close();
  }
}
