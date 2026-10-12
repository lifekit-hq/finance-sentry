import {type AsyncStateStatus} from '@lifekit-hq/ui';

export interface AsyncViewInput {
  isLoading: boolean;
  /** The screen already holds data from an earlier successful load. */
  hasData: boolean;
  /** Empty when the last request did not fail. */
  errorMessage: string;
  offline: boolean;
  /** Epoch ms of the last successful load, or null when none has landed. */
  lastSyncedAt: Nullable<number>;
}

export interface AsyncView {
  status: AsyncStateStatus;
  errorMessage: string;
  /** Set while offline with data on screen: the stale data stays, with this notice above it. */
  offlineNotice: Nullable<string>;
}
