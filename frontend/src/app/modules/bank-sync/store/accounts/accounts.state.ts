import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {type ManualHolding} from '../../models/manual-holding/manual-holding.model';

export interface AccountsState {
  summary: Nullable<WealthSummaryResponse>;
  /**
   * Every account `GET /accounts` lists, retired ones included: the wealth summary leaves a closed account out, so
   * the "Closed accounts" section and the notes on live rows (card expiry, replaced card) read from here.
   */
  allAccounts: BankAccount[];
  /** Values the person entered by hand (`GET /wealth/manual-holdings`); the summary carries their total, not the fields an edit needs. */
  manualHoldings: ManualHolding[];
  /** Epoch ms of the last successful summary load; drives the offline "last synced" notice. */
  lastSyncedAt: Nullable<number>;
  /** Bumped after every successful disconnect so sibling stores (e.g. HoldingsStore) can react. */
  disconnectVersion: number;
  /**
   * Open (true) or closed (false) by `AccountsDisclosureUtils.key`, for the groups this device toggled; a
   * group absent here follows its category's default (`AccountsDisclosureUtils.defaultOpen`).
   */
  institutionOpen: Record<string, boolean>;
  /** Provider a `?provider=` link named: its institutions open once the summary lists them, then it clears. */
  focusProvider: Nullable<string>;
}

export const initialAccountsState: AccountsState = {
  summary: null,
  allAccounts: [],
  manualHoldings: [],
  lastSyncedAt: null,
  disconnectVersion: 0,
  institutionOpen: {},
  focusProvider: null,
};
