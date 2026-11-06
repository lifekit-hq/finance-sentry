import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type IncomeSource} from '../../models/income-source/income-source.model';
import {type IncomeSourcesState} from './income-sources.state';

export function incomeSourcesMethods(store: WritableStateSource<IncomeSourcesState>) {
  return {
    setSources(sources: IncomeSource[]): void {
      patchState(store, {sources});
    },
    setBusy(id: string): void {
      patchState(store, {busyId: id, actionErrorCode: null});
    },
    setIdle(): void {
      patchState(store, {busyId: null, actionErrorCode: null});
    },
    setActionError(errorCode: Nullable<string>): void {
      patchState(store, {busyId: null, actionErrorCode: errorCode});
    },
    clearActionError(): void {
      patchState(store, {actionErrorCode: null});
    },
  };
}
