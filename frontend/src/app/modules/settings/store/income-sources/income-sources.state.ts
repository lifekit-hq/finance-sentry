import {type Nullable} from '@lifekit-hq/ui';

import {type IncomeSource} from '../../models/income-source/income-source.model';

export interface IncomeSourcesState {
  sources: IncomeSource[];
  /** The source a retire or delete is in flight for. */
  busyId: Nullable<string>;
  actionErrorCode: Nullable<string>;
}

export const initialIncomeSourcesState: IncomeSourcesState = {
  sources: [],
  busyId: null,
  actionErrorCode: null,
};
