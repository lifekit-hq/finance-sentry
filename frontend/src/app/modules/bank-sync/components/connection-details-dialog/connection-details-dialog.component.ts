import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {CMN_DIALOG_DATA} from '@lifekit-hq/ui';

import {type ConnectionDetailsDialogData} from '../../models/connection-details/connection-details.model';

/** Read-only body of the "Connection details" sheet opened from an institution's or a closed account's menu. */
@Component({
  selector: 'fns-connection-details-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './connection-details-dialog.component.html',
})
export class ConnectionDetailsDialogComponent {
  public readonly data = inject<ConnectionDetailsDialogData>(CMN_DIALOG_DATA);
}
