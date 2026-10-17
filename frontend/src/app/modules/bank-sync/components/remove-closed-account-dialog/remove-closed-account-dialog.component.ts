import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {ButtonComponent, CMN_DIALOG_DATA, DialogActionsComponent} from '@lifekit-hq/ui';

import {type RemoveClosedAccountDialogData} from '../../models/remove-closed-account-dialog/remove-closed-account-dialog.model';

@Component({
  selector: 'fns-remove-closed-account-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ButtonComponent, DialogActionsComponent],
  templateUrl: './remove-closed-account-dialog.component.html',
})
export class RemoveClosedAccountDialogComponent {
  private readonly dialogRef = inject<DialogRef<boolean>>(DialogRef);

  public readonly data = inject<RemoveClosedAccountDialogData>(CMN_DIALOG_DATA);

  public confirm(): void {
    this.dialogRef.close(true);
  }

  public cancel(): void {
    this.dialogRef.close(false);
  }
}
