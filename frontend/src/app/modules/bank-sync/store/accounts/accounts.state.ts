import {type WealthSummaryResponse} from '../../../../shared/models/wealth/wealth.model';

export interface AccountsState {
  summary: Nullable<WealthSummaryResponse>;
  /** Epoch ms of the last successful summary load; drives the offline "last synced" notice. */
  lastSyncedAt: Nullable<number>;
  /** Bumped after every successful disconnect so sibling stores (e.g. HoldingsStore) can react. */
  disconnectVersion: number;
}

export const initialAccountsState: AccountsState = {
  summary: null,
  lastSyncedAt: null,
  disconnectVersion: 0,
};
