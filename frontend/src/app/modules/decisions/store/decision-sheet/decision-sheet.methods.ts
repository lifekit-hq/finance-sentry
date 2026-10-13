import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type DecisionSheetState, type DecisionSheetStatus} from './decision-sheet.state';

export function decisionSheetMethods(store: WritableStateSource<DecisionSheetState>) {
  return {
    setStatus(status: DecisionSheetStatus): void {
      patchState(store, {status, errorCode: null});
    },
    setError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode});
    },
  };
}
