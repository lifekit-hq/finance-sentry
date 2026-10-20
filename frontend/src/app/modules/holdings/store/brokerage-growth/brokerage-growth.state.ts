import {type Nullable} from '@lifekit-hq/ui';

import {type BrokerageGrowth} from '../../models/brokerage-growth/brokerage-growth.model';

export interface BrokerageGrowthState {
  growth: Nullable<BrokerageGrowth>;
}

export const initialBrokerageGrowthState: BrokerageGrowthState = {
  growth: null,
};
