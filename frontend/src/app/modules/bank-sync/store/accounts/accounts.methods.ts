import {getState, patchState, type WritableStateSource} from '@ngrx/signals';

import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';
import {type AccountsState} from './accounts.state';

export function accountsMethods(store: WritableStateSource<AccountsState>) {
  return {
    setSummary(summary: WealthSummaryResponse): void {
      patchState(store, {summary, lastSyncedAt: Date.now()});
    },
    bumpDisconnectVersion(): void {
      patchState(store, {disconnectVersion: getState(store).disconnectVersion + 1});
    },
  };
}
