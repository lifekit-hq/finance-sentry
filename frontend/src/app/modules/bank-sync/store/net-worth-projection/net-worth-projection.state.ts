import {type Nullable} from '@lifekit-hq/ui';

import {type NetWorthProjection} from '../../models/projection/projection.model';

export interface NetWorthProjectionState {
  projection: Nullable<NetWorthProjection>;
}

export const initialNetWorthProjectionState: NetWorthProjectionState = {
  projection: null,
};
