import {type Nullable} from '@lifekit-hq/ui';

import {type GoalsProgress} from '../../models/goals-progress/goals-progress.model';

export interface GoalsProgressState {
  progress: Nullable<GoalsProgress>;
}

export const initialGoalsProgressState: GoalsProgressState = {
  progress: null,
};
