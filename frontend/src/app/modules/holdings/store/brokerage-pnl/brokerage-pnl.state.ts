import {type Nullable} from '@lifekit-hq/ui';

import {type BrokeragePnl} from '../../models/brokerage-pnl/brokerage-pnl.model';

export interface BrokeragePnlState {
  pnl: Nullable<BrokeragePnl>;
}

export const initialBrokeragePnlState: BrokeragePnlState = {
  pnl: null,
};
