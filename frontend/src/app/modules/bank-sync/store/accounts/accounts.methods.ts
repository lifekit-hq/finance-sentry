import {getState, patchState, type WritableStateSource} from '@ngrx/signals';

import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {type ManualHolding} from '../../models/manual-holding/manual-holding.model';
import {AccountsDisclosureUtils} from '../../utils/accounts-disclosure.utils';
import {type AccountsState} from './accounts.state';

export function accountsMethods(store: WritableStateSource<AccountsState>) {
  // Opens every institution of the focused provider the summary lists, through the remembered-disclosure
  // record, then clears the focus; with no summary yet it waits for the one `setSummary` brings.
  const applyFocus = (): void => {
    const {focusProvider, summary, institutionOpen} = getState(store);
    if (!focusProvider || !summary) {
      return;
    }
    const opened = Object.fromEntries(
      summary.categories
        .flatMap(category => category.institutions)
        .filter(institution => institution.provider === focusProvider)
        .map(institution => [AccountsDisclosureUtils.key(institution), true] as const)
    );
    patchState(store, {focusProvider: null, institutionOpen: {...institutionOpen, ...opened}});
    AccountsDisclosureUtils.write(getState(store).institutionOpen);
  };

  return {
    setSummary(summary: WealthSummaryResponse): void {
      patchState(store, {summary, lastSyncedAt: Date.now()});
      applyFocus();
    },
    openProvider(provider: Nullable<string>): void {
      patchState(store, {focusProvider: provider});
      applyFocus();
    },
    setAllAccounts(allAccounts: BankAccount[]): void {
      patchState(store, {allAccounts});
    },
    setManualHoldings(manualHoldings: ManualHolding[]): void {
      patchState(store, {manualHoldings});
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
