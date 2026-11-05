import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type GoalsProgress} from '../../models/goals-progress/goals-progress.model';
import {type GoalsProgressState} from './goals-progress.state';

export function goalsProgressMethods(store: WritableStateSource<GoalsProgressState>) {
  return {
    setProgress(progress: GoalsProgress): void {
      patchState(store, {progress});
    },
  };
}
