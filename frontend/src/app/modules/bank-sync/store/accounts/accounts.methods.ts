import {getState, patchState, type WritableStateSource} from '@ngrx/signals';

import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';
import {AccountsDisclosureUtils} from '../../utils/accounts-disclosure.utils';
import {type AccountsState} from './accounts.state';

export function accountsMethods(store: WritableStateSource<AccountsState>) {
  return {
    setSummary(summary: WealthSummaryResponse): void {
      patchState(store, {summary, lastSyncedAt: Date.now()});
    },
    restoreCollapsed(): void {
      patchState(store, {collapsedInstitutions: AccountsDisclosureUtils.read()});
    },
    setInstitutionOpen(key: string, isOpen: boolean): void {
      const rest = getState(store).collapsedInstitutions.filter(item => item !== key);
      const collapsedInstitutions = isOpen ? rest : [...rest, key];
      patchState(store, {collapsedInstitutions});
      AccountsDisclosureUtils.write(collapsedInstitutions);
    },
    bumpDisconnectVersion(): void {
      patchState(store, {disconnectVersion: getState(store).disconnectVersion + 1});
    },
  };
}
