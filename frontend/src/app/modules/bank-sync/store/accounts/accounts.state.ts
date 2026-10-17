import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';

export interface AccountsState {
  summary: Nullable<WealthSummaryResponse>;
  /** Epoch ms of the last successful summary load; drives the offline "last synced" notice. */
  lastSyncedAt: Nullable<number>;
  /** Bumped after every successful disconnect so sibling stores (e.g. HoldingsStore) can react. */
  disconnectVersion: number;
  /**
   * Open (true) or closed (false) by `AccountsDisclosureUtils.key`, for the groups this device toggled; a
   * group absent here follows its category's default (`AccountsDisclosureUtils.defaultOpen`).
   */
  institutionOpen: Record<string, boolean>;
}

export const initialAccountsState: AccountsState = {
  summary: null,
  lastSyncedAt: null,
  disconnectVersion: 0,
  institutionOpen: {},
};
