import {withAsyncStatus, withUrlSync} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {
  ISO_DATE_PARAM_CODEC,
  MONTH_KEY_PARAM_CODEC,
  MONTHS_PARAM_CODEC,
} from '../../constants/url-params/url-params.constants';
import {flowBreakdownComputed} from './flow-breakdown.computed';
import {flowBreakdownEffects, flowBreakdownHooks} from './flow-breakdown.effects';
import {flowBreakdownMethods} from './flow-breakdown.methods';
import {initialFlowBreakdownState} from './flow-breakdown.state';

export const FlowBreakdownStore = signalStore(
  withState(initialFlowBreakdownState),
  withUrlSync({
    selectedMonth: {
      param: 'month',
      default: initialFlowBreakdownState.selectedMonth,
      codec: MONTH_KEY_PARAM_CODEC,
    },
    from: {param: 'from', default: initialFlowBreakdownState.from, codec: ISO_DATE_PARAM_CODEC},
    to: {param: 'to', default: initialFlowBreakdownState.to, codec: ISO_DATE_PARAM_CODEC},
    months: {
      param: 'months',
      default: initialFlowBreakdownState.months,
      codec: MONTHS_PARAM_CODEC,
    },
  }),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the month breakdown. Please try again.'}),
  withMethods(flowBreakdownMethods),
  withComputed(flowBreakdownComputed),
  withMethods(flowBreakdownEffects),
  withHooks({onInit: flowBreakdownHooks})
);
