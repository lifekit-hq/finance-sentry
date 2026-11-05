import {type Nullable} from '@lifekit-hq/ui';

import {type CashflowProfile} from '../../models/cashflow-profile/cashflow-profile.model';

export interface CashflowProfileState {
  profile: Nullable<CashflowProfile>;
}

export const initialCashflowProfileState: CashflowProfileState = {
  profile: null,
};
