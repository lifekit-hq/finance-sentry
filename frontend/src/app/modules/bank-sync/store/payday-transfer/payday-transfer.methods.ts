import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type PaydayTransfer} from '../../models/payday-transfer/payday-transfer.model';
import {type PaydayTransferState} from './payday-transfer.state';

export function paydayTransferMethods(store: WritableStateSource<PaydayTransferState>) {
  return {
    setTransfer(transfer: PaydayTransfer): void {
      patchState(store, {transfer});
    },
    setEditing(editing: boolean): void {
      patchState(store, {editing});
    },
  };
}
