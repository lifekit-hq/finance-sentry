import {type Nullable} from '@lifekit-hq/ui';
export interface SyncStatusResponse {
  status: 'pending' | 'running' | 'success' | 'failed';
  transactionCountFetched: number;
  transactionCountDeduped: number;
  errorMessage: Nullable<string>;
  lastSyncTimestamp: Nullable<string>;
  startedAt: Nullable<string>;
  completedAt: Nullable<string>;
}

export interface TriggerSyncResponse {
  jobId: string;
  message: string;
}
