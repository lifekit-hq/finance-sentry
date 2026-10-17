import {DialogRef} from '@angular/cdk/dialog';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {
  ButtonComponent,
  CMN_DIALOG_DATA,
  CMN_DIALOG_PRESENTATION,
  DialogActionsComponent,
} from '@lifekit-hq/ui';

import {type DisconnectDialogData} from '../../models/disconnect-dialog/disconnect-dialog.model';

@Component({
  selector: 'fns-disconnect-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ButtonComponent, DialogActionsComponent],
  templateUrl: './disconnect-dialog.component.html',
})
export class DisconnectDialogComponent {
  private readonly dialogRef = inject<DialogRef<boolean>>(DialogRef);

  private readonly presentation = inject(CMN_DIALOG_PRESENTATION, {optional: true});

  public readonly data = inject<DisconnectDialogData>(CMN_DIALOG_DATA);

  /** An action sheet draws its own Cancel button, so the dialog drops its own. */
  public readonly actionSheet = computed(() => this.presentation?.() === 'action-sheet');

  public confirm(): void {
    this.dialogRef.close(true);
  }

  public cancel(): void {
    this.dialogRef.close(false);
  }
}
