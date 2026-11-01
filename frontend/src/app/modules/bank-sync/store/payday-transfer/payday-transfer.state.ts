import {type Nullable} from '@lifekit-hq/ui';

import {type PaydayTransfer} from '../../models/payday-transfer/payday-transfer.model';

export interface PaydayTransferState {
  transfer: Nullable<PaydayTransfer>;
  editing: boolean;
}

export const initialPaydayTransferState: PaydayTransferState = {
  transfer: null,
  editing: false,
};
