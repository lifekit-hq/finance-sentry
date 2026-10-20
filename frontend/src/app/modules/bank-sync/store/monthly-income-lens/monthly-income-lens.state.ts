import {type Nullable} from '@lifekit-hq/ui';

import {type MonthlyIncomeLens} from '../../models/income-lens/income-lens.model';

export interface MonthlyIncomeLensState {
  lens: Nullable<MonthlyIncomeLens>;
}

export const initialMonthlyIncomeLensState: MonthlyIncomeLensState = {
  lens: null,
};
