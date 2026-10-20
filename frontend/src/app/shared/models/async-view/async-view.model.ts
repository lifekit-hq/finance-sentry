import {type AsyncStateStatus, type Nullable} from '@lifekit-hq/ui';

export interface AsyncViewInput {
  isLoading: boolean;
  /** The screen already holds data from an earlier successful load. */
  hasData: boolean;
  /** Empty when the last request did not fail. */
  errorMessage: string;
  offline: boolean;
  /** Epoch ms of the last successful load, or null when none has landed. */
  lastSyncedAt: Nullable<number>;
  /**
   * The region sets `keepContent` on `cmn-async-state`: a reload over data on screen reports
   * `loading`, so the primitive keeps that data, dimmed, instead of reading it as settled.
   */
  keepContent?: boolean;
}

export interface AsyncView {
  status: AsyncStateStatus;
  errorMessage: string;
  /** Set while offline with data on screen: the stale data stays, with this notice above it. */
  offlineNotice: Nullable<string>;
}
