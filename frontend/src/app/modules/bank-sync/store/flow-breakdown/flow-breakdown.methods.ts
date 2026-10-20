import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type FlowBreakdown} from '../../models/flow-breakdown/flow-breakdown.model';
import {type FlowBreakdownState} from './flow-breakdown.state';

export function flowBreakdownMethods(store: WritableStateSource<FlowBreakdownState>) {
  return {
    setBreakdown(breakdown: FlowBreakdown): void {
      patchState(store, {breakdown});
    },
    setAccountFilter(accountId: Nullable<string>): void {
      patchState(store, state => ({
        accountFilter: state.accountFilter === accountId ? null : accountId,
      }));
    },
  };
}
