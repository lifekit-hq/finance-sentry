import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {GOALS_PROGRESS_ERROR_MESSAGE} from '../../constants/goals-progress/goals-progress.constants';
import {goalsProgressComputed} from './goals-progress.computed';
import {goalsProgressEffects, goalsProgressHooks} from './goals-progress.effects';
import {goalsProgressMethods} from './goals-progress.methods';
import {initialGoalsProgressState} from './goals-progress.state';

export const GoalsProgressStore = signalStore(
  withState(initialGoalsProgressState),
  withAsyncStatus({defaultErrorMessage: GOALS_PROGRESS_ERROR_MESSAGE}),
  withMethods(goalsProgressMethods),
  withComputed(goalsProgressComputed),
  withMethods(goalsProgressEffects),
  withHooks({onInit: goalsProgressHooks})
);
