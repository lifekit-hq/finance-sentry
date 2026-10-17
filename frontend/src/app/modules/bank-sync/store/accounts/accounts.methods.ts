import {getState, patchState, type WritableStateSource} from '@ngrx/signals';

import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {AccountsDisclosureUtils} from '../../utils/accounts-disclosure.utils';
import {type AccountsState} from './accounts.state';

export function accountsMethods(store: WritableStateSource<AccountsState>) {
  return {
    setSummary(summary: WealthSummaryResponse): void {
      patchState(store, {summary, lastSyncedAt: Date.now()});
    },
    setAllAccounts(allAccounts: BankAccount[]): void {
      patchState(store, {allAccounts});
    },
    restoreInstitutionOpen(): void {
      patchState(store, {institutionOpen: AccountsDisclosureUtils.read()});
    },
    setInstitutionOpen(key: string, isOpen: boolean): void {
      const institutionOpen = {...getState(store).institutionOpen, [key]: isOpen};
      patchState(store, {institutionOpen});
      AccountsDisclosureUtils.write(institutionOpen);
    },
    bumpDisconnectVersion(): void {
      patchState(store, {disconnectVersion: getState(store).disconnectVersion + 1});
    },
  };
}
