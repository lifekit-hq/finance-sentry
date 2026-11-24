import {type Nullable} from '@lifekit-hq/ui';

import {type SleeveCosts} from '../../models/cost-of-investing/cost-of-investing.model';

export interface CostOfInvestingState {
  costs: Nullable<SleeveCosts>;
}

export const initialCostOfInvestingState: CostOfInvestingState = {
  costs: null,
};
