import {
  type DashboardData,
  type HistoryRange,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';

export interface DashboardState {
  data: Nullable<DashboardData>;
  /** Epoch ms of the last successful dashboard load; drives the offline "last synced" notice. */
  lastSyncedAt: Nullable<number>;
  netWorthHistory: NetWorthSnapshotDto[];
  historyRange: HistoryRange;
  historyHasHistory: boolean;
  historyLoading: boolean;
  historyError: string | null;
  /** Index into the drawn history while a pointer scrubs the hero chart; null at rest. */
  scrubIndex: number | null;
}

export const initialDashboardState: DashboardState = {
  data: null,
  lastSyncedAt: null,
  netWorthHistory: [],
  historyRange: '3m',
  historyHasHistory: false,
  historyLoading: false,
  historyError: null,
  scrubIndex: null,
};
