import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type FlowBreakdown,
  type FlowBreakdownRequest,
} from '../../models/flow-breakdown/flow-breakdown.model';
import {type FlowBreakdownState} from './flow-breakdown.state';

export function flowBreakdownMethods(store: WritableStateSource<FlowBreakdownState>) {
  return {
    setLoading(request: FlowBreakdownRequest): void {
      patchState(
        store,
        request.kind === 'month'
          ? {month: request.month, range: null, status: 'loading', errorCode: null}
          : {month: '', range: request, status: 'loading', errorCode: null}
      );
    },
    setBreakdown(breakdown: FlowBreakdown): void {
      patchState(store, {breakdown, status: 'idle', errorCode: null});
    },
    setError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode});
    },
    setAccountFilter(accountId: Nullable<string>): void {
      patchState(store, state => ({
        accountFilter: state.accountFilter === accountId ? null : accountId,
      }));
    },
  };
}
