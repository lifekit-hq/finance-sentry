import {withAsyncStatus, withUrlSync} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {HISTORY_RANGE_CODEC} from '../../constants/dashboard/dashboard.constants';
import {dashboardComputed} from './dashboard.computed';
import {dashboardEffects, dashboardHooks} from './dashboard.effects';
import {dashboardMethods} from './dashboard.methods';
import {initialDashboardState} from './dashboard.state';

export const DashboardStore = signalStore(
  withState(initialDashboardState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load dashboard data. Please try again.'}),
  withUrlSync({
    historyRange: {
      param: 'range',
      default: initialDashboardState.historyRange,
      codec: HISTORY_RANGE_CODEC,
    },
  }),
  withMethods(dashboardMethods),
  withComputed(dashboardComputed),
  withMethods(dashboardEffects),
  withHooks({onInit: dashboardHooks})
);
